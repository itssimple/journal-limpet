using Hangfire.Server;
using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.Journal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Polly;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs
{
    public class JournalDownloader
    {
        public static async Task DownloadJournalAsync(Guid userIdentifier, PerformContext context)
        {
            using (var rlock = new RedisJobLock($"JournalDownloader.DownloadJournal.{userIdentifier}"))
            {
                if (!rlock.TryTakeLock()) return;

                using (var scope = Startup.ServiceProvider.CreateScope())
                {
                    MSSQLDB db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();
                    IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                    MinioClient minioClient = scope.ServiceProvider.GetRequiredService<MinioClient>();

                    var user = (await db.ExecuteListAsync<Shared.Models.User.Profile>(
    @"SELECT * FROM user_profile WHERE user_identifier = @user_identifier AND last_notification_mail IS NULL AND deleted = 0",
    new SqlParameter("user_identifier", userIdentifier)
                )).FirstOrDefault();

                    if (user == null) return;

                    var authToken = user.UserSettings.AuthToken;

                    IHttpClientFactory _hcf = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

                    var hc = _hcf.CreateClient();

                    hc.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
                    hc.BaseAddress = new Uri("https://companion.orerve.net");

                    DateTime journalDate = DateTime.Today.AddDays(-25);

                    while (journalDate.Date != DateTime.Today)
                    {
                        if (!await TryGetJournalAsync(configuration, journalDate, user, db, hc, minioClient))
                        {
                            // Failed to get loop journal
                        }

                        journalDate = journalDate.AddDays(1);

                    }

                    if (!await TryGetJournalAsync(configuration, journalDate, user, db, hc, minioClient))
                    {
                        // Failed to get todays journal
                    }
                }
            }
        }

        static async Task SendAdminNotification(IConfiguration configuration, string subject, string mailBody)
        {
            var sendgridClient = new SendGridClient(configuration["SendGrid:ApiKey"]);
            var mail = MailHelper.CreateSingleEmail(
                new EmailAddress("no-reply+error-notifications@journal-limpet.com", "Journal Limpet"),
                new EmailAddress(configuration["ErrorMail"]),
                subject,
                mailBody,
                mailBody.Replace("\n", "<br />\n")
            );

            await sendgridClient.SendEmailAsync(mail);
        }

        static async Task<bool> TryGetJournalAsync(IConfiguration configuration, DateTime journalDate, Shared.Models.User.Profile user, MSSQLDB db, HttpClient hc, MinioClient minioClient)
        {
            try
            {
                var res = await GetJournalAsync(configuration, journalDate, user, db, hc, minioClient);
                int loop_counter = 0;

                while (res.code != HttpStatusCode.OK)
                {
                    Thread.Sleep(5000);
                    if (loop_counter > 10)
                    {
                        await SendAdminNotification(
                            configuration,
                            "Failed to download journal",
$@"Hey NLK,

Got response code: {res.code} while trying to grab journals.

Response:

{await res.message.Content.ReadAsStringAsync()}"
                        );
                        return false;
                    }

                    switch (res.code)
                    {
                        case HttpStatusCode.PartialContent:
                            Thread.Sleep(1000);
                            res = await GetJournalAsync(configuration, journalDate, user, db, hc, minioClient);
                            break;
                    }
                    loop_counter++;
                }
            }
            catch (TooManyOldJournalItemsException ex)
            {
                await SendAdminNotification(configuration, "Exception: Too many old journal items", ex.ToString());
                return false;
            }
            catch (Exception ex)
            {
                var errorMessage = ex.ToString() + "\n\n" + JsonSerializer.Serialize(user, new JsonSerializerOptions() { WriteIndented = true });

                await SendAdminNotification(configuration, "Exception", errorMessage);
                return false;
            }

            return true;
        }

        static async Task<(HttpStatusCode code, HttpResponseMessage message)> GetJournalAsync(IConfiguration configuration, DateTime journalDate, Shared.Models.User.Profile user, MSSQLDB db, HttpClient hc, MinioClient minioClient)
        {
            var oldJournalRow = await db.ExecuteListAsync<UserJournal>(
                "SELECT TOP 1 * FROM user_journal WHERE user_identifier = @user_identifier AND journal_date = @journal_date",
                new SqlParameter("user_identifier", user.UserIdentifier),
                new SqlParameter("journal_date", journalDate)
            );

            if (oldJournalRow.Count > 1)
            {
                throw new TooManyOldJournalItemsException(journalDate, user.UserIdentifier);
            }

            var previousRow = oldJournalRow.FirstOrDefault();

            if (previousRow?.CompleteEntry ?? false)
            {
                return (HttpStatusCode.OK, null);
            }

            var pollicy = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(r => r.StatusCode == HttpStatusCode.PartialContent)
                .WaitAndRetryAsync(100, attempt => TimeSpan.FromSeconds(1));

            var journalRequest = await pollicy.ExecuteAsync(() => hc.GetAsync($"/journal/{journalDate.Year}/{journalDate.Month}/{journalDate.Day}"));

            var journalContent = await journalRequest.Content.ReadAsStringAsync();

            if (!journalRequest.IsSuccessStatusCode || journalRequest.StatusCode == HttpStatusCode.PartialContent)
            {
                return (journalRequest.StatusCode, journalRequest);
            }

            var journalRows = journalContent.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);

            bool updateFileOnS3 = (previousRow?.LastProcessedLineNumber ?? 0) != journalRows.Length && (previousRow?.LastProcessedLine != (journalRows.LastOrDefault() ?? string.Empty));

            if (!string.IsNullOrWhiteSpace(journalContent))
            {
                var firstRow = journalRows.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstRow))
                {
                    var row = JsonDocument.Parse(firstRow).RootElement;

                    var apiFileHeader = new
                    {
                        Timestamp = row.GetProperty("timestamp").GetString(),
                        Event = "JournalLimpetFileheader",
                        Description = "Missing fileheader from cAPI journal"
                    };

                    var serializedApiFileHeader = JsonSerializer.Serialize(apiFileHeader, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    serializedApiFileHeader = serializedApiFileHeader.Insert(serializedApiFileHeader.Length - 1, " ").Insert(1, " ");

                    journalContent =
                        serializedApiFileHeader +
                        "\n" +
                        journalContent;
                }
            }

            var journalBytes = Encoding.UTF8.GetBytes(journalContent);
            string fileName = $"{user.UserIdentifier}/journal/{journalDate.Year}/{journalDate.Month.ToString().PadLeft(2, '0')}/{journalDate.Day.ToString().PadLeft(2, '0')}.journal";

            if (updateFileOnS3)
            {
                using (var ms = new MemoryStream(journalBytes))
                {
                    await minioClient.PutObjectAsync("journal-limpet", fileName, ms, ms.Length, "text/jsonl");
                }
            }

            if (previousRow == null)
            {
                await db.ExecuteNonQueryAsync(@"INSERT INTO user_journal (user_identifier, journal_date, s3_path, last_processed_line, last_processed_line_number, complete_entry, last_update)
VALUES (@user_identifier, @journal_date, @s3_path, @last_processed_line, @last_processed_line_number, @complete_entry, GETUTCDATE())",
    new SqlParameter("user_identifier", user.UserIdentifier),
    new SqlParameter("journal_date", journalDate),
    new SqlParameter("s3_path", fileName),
    new SqlParameter("last_processed_line", journalRows.LastOrDefault() ?? string.Empty),
    new SqlParameter("last_processed_line_number", journalRows.Length),
    new SqlParameter("complete_entry", DateTime.UtcNow.Date > journalDate.Date)
);
            }
            else
            {
                await db.ExecuteNonQueryAsync(@"UPDATE user_journal SET
last_processed_line = @last_processed_line,
last_processed_line_number = @last_processed_line_number,
complete_entry = @complete_entry,
last_update = GETUTCDATE()
WHERE journal_id = @journal_id AND user_identifier = @user_identifier",
    new SqlParameter("journal_id", previousRow.JournalId),
    new SqlParameter("user_identifier", user.UserIdentifier),
    new SqlParameter("last_processed_line", journalRows.LastOrDefault() ?? string.Empty),
    new SqlParameter("last_processed_line_number", journalRows.Length),
    new SqlParameter("complete_entry", DateTime.UtcNow.Date > journalDate.Date)
);
            }

            Thread.Sleep(5000);
            return (HttpStatusCode.OK, journalRequest);
        }
    }
}

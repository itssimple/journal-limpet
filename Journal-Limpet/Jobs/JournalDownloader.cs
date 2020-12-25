using Amazon;
using Amazon.Runtime;
using Amazon.S3.Transfer;
using Hangfire.Server;
using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.Journal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

                    DateTime journalDate = DateTime.Today.AddDays(-30);

                    while (journalDate.Date != DateTime.Today)
                    {
                        if (!await TryGetJournalAsync(configuration, journalDate, user, db, hc))
                        {
                            // Failed to get loop journal
                        }

                        journalDate = journalDate.AddDays(1);

                    }

                    if (!await TryGetJournalAsync(configuration, journalDate, user, db, hc))
                    {
                        // Failed to get todays journal
                    }
                }
            }
        }

        static async Task SendAdminNotification(IConfiguration configuration, string subject, string mailBody, string mailBodyHtml)
        {
            var sendgridClient = new SendGridClient(configuration["SendGrid:ApiKey"]);
            var mail = MailHelper.CreateSingleEmail(
                new EmailAddress("no-reply+error-notifications@journal-limpet.com", "Journal Limpet"),
                new EmailAddress(configuration["ErrorMail"]),
                subject,
                mailBody,
                mailBodyHtml
            );

            await sendgridClient.SendEmailAsync(mail);
        }

        static async Task<bool> TryGetJournalAsync(IConfiguration configuration, DateTime journalDate, Shared.Models.User.Profile user, MSSQLDB db, HttpClient hc)
        {
            try
            {
                var res = await GetJournalAsync(journalDate, user, db, hc);
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

{await res.message.Content.ReadAsStringAsync()}",
$@"Hey NLK,<br />
<br />
Got response code: {res.code} while trying to grab journals.<br />
<br />
Response:<br />
<br />
{await res.message.Content.ReadAsStringAsync()}"
                        );
                        return false;
                    }

                    switch (res.code)
                    {
                        case HttpStatusCode.Unauthorized:
                            // Should never be unauthorized, so lets just sleep 5 seconds extra, for shits and giggles
                            Thread.Sleep(5000);
                            res = await GetJournalAsync(journalDate, user, db, hc);
                            break;
                        case HttpStatusCode.PartialContent:
                            res = await GetJournalAsync(journalDate, user, db, hc);
                            break;
                    }
                    loop_counter++;
                }
            }
            catch (TooManyOldJournalItemsException ex)
            {
                await SendAdminNotification(configuration, "Exception: Too many old journal items", ex.ToString(), ex.ToString());
                return false;
            }
            catch (Exception ex)
            {
                await SendAdminNotification(configuration, "Exception", ex.ToString(), ex.ToString());
                return false;
            }

            return true;
        }

        static async Task<(HttpStatusCode code, HttpResponseMessage message)> GetJournalAsync(DateTime journalDate, Shared.Models.User.Profile user, MSSQLDB db, HttpClient hc)
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

            using (var s3Client = new Amazon.S3.AmazonS3Client(new BasicAWSCredentials("AKIAS4FUTBCKC2GVHG4J", "xvYC5P3VzgwByKLe8LXZJyAYpOJyKNH/vnmg/zK6"), RegionEndpoint.EUNorth1))
            {
                using (var tu = new TransferUtility(s3Client))
                {
                    var journalRequest = await hc.GetAsync($"/journal/{journalDate.Year}/{journalDate.Month}/{journalDate.Day}");
                    switch (journalRequest.StatusCode)
                    {
                        case HttpStatusCode.Unauthorized:
                            // Just do a quick re-auth here, if possible, otherwise throw exception
                            return (journalRequest.StatusCode, journalRequest);
                        case HttpStatusCode.PartialContent:
                            // Continue to fetch until we get a 200 status
                            return (journalRequest.StatusCode, journalRequest);
                        case HttpStatusCode.NoContent:
                        // Continue on, no journal stored for this date
                        case HttpStatusCode.OK:
                            // We got the entire journal! Lets save this to S3
                            break;
                        default:
                            throw new Exception($"Status error: {journalRequest.StatusCode}\n{(await journalRequest.Content.ReadAsStringAsync())}");
                    }

                    var journalContent = await journalRequest.Content.ReadAsStringAsync();

                    var journalRows = journalContent.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);

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
                                Environment.NewLine +
                                journalContent;
                        }
                    }

                    var journalBytes = Encoding.UTF8.GetBytes(journalContent);
                    string fileName = $"{user.UserIdentifier}/journal/{journalDate.Year}/{journalDate.Month.ToString().PadLeft(2, '0')}/{journalDate.Day.ToString().PadLeft(2, '0')}.journal";

                    using (var ms = new MemoryStream(journalBytes))
                    {
                        await tu.UploadAsync(ms, "journal-limpet", fileName);
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
    }
}

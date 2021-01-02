using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models.Journal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Polly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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

                context.WriteLine($"Looking for journals for user {userIdentifier}");

                using (var scope = Startup.ServiceProvider.CreateScope())
                {
                    MSSQLDB db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();
                    IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                    MinioClient minioClient = scope.ServiceProvider.GetRequiredService<MinioClient>();

                    var discordClient = scope.ServiceProvider.GetRequiredService<DiscordWebhook>();

                    var user = (await db.ExecuteListAsync<Shared.Models.User.Profile>(
    @"SELECT * FROM user_profile WHERE user_identifier = @user_identifier AND last_notification_mail IS NULL AND deleted = 0 AND skip_download = 0",
    new SqlParameter("user_identifier", userIdentifier)
                )).FirstOrDefault();

                    if (user == null) return;

                    var authToken = user.UserSettings.AuthToken;

                    IHttpClientFactory _hcf = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

                    var hc = _hcf.CreateClient();

                    hc.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
                    hc.BaseAddress = new Uri("https://companion.orerve.net");

                    var profile = await GetProfileAsync(hc);

                    if (!profile.IsSuccessStatusCode)
                    {
                        switch (profile.StatusCode)
                        {
                            case HttpStatusCode.BadRequest:
                                // User does not potentially own the game
                                break;
                            case HttpStatusCode.Unauthorized:
                                // Invalid token (or Epic games)
                                break;
                        }

                        await SSEActivitySender.SendUserActivityAsync(user.UserIdentifier,
                            "Could not authorize you",
                            "Sorry, but there seems to be something wrong with your account. Please contact us so we can try and figure out what's wrong!"
                        );

                        context.WriteLine("Bailing out early, user doesn't own Elite or has issues with cAPI auth");
                        return;
                    }

                    DateTime journalDate = DateTime.Today.AddDays(-25);

                    await SSEActivitySender.SendUserActivityAsync(user.UserIdentifier,
                            "Downloading your journals",
                            "We're beginning to download your journals now, a few notifications may pop up."
                        );

                    while (journalDate.Date != DateTime.Today)
                    {
                        context.WriteLine($"Fetching data for {journalDate.ToString("yyyy-MM-dd")}");
                        var req = await TryGetJournalAsync(discordClient, journalDate, user, db, hc, minioClient);
                        if (req.shouldBail)
                        {
                            // Failed to get loop journal
                            context.WriteLine($"Bailing because of errors");
                            return;
                        }

                        journalDate = journalDate.AddDays(1);
                    }

                    context.WriteLine($"Fetching data for {journalDate.ToString("yyyy-MM-dd")}");
                    var reqOut = await TryGetJournalAsync(discordClient, journalDate, user, db, hc, minioClient);

                    if (reqOut.shouldBail)
                    {
                        // Failed to get loop journal
                        context.WriteLine($"Bailing because of errors");
                        return;
                    }

                    context.WriteLine("All done!");
                }
            }
        }

        public static async Task<HttpResponseMessage> GetProfileAsync(HttpClient hc)
        {
            return await hc.GetAsync($"/profile");
        }

        static async Task SendAdminNotification(DiscordWebhook discord, string message, string description, Dictionary<string, string> fields = null)
        {

            await discord.SendMessageAsync(message, new List<DiscordWebhookEmbed>
            {
                new DiscordWebhookEmbed
                {
                    Description = description,
                    Fields = (fields ?? new Dictionary<string, string>()).Select(k => new DiscordWebhookEmbedField { Name = k.Key, Value = k.Value }).ToList()
                }
            });
        }

        static async Task<(bool failedRequest, bool shouldBail)> TryGetJournalAsync(DiscordWebhook discord, DateTime journalDate, Shared.Models.User.Profile user, MSSQLDB db, HttpClient hc, MinioClient minioClient)
        {
            try
            {
                var res = await GetJournalAsync(journalDate, user, db, hc, minioClient);
                int loop_counter = 0;

                while (res.code != HttpStatusCode.OK)
                {
                    Thread.Sleep(5000);

                    var content = await res.message.Content.ReadAsStringAsync();

                    if (content.Contains("to purchase Elite: Dangerous"))
                    {
                        await db.ExecuteNonQueryAsync("UPDATE user_profile SET skip_download = 1 WHERE user_identifier = @user_identifier", new SqlParameter("user_identifier", user.UserIdentifier));
                        await SendAdminNotification(
                            discord,
                            "Failed to download journal, cannot access cAPI",
                            "User probably has a Epic Games account",
                            new Dictionary<string, string>
                            {
                                { "Response code", res.code.ToString() },
                                { "Content", content },
                                { "User identifier", user.UserIdentifier.ToString() },
                                { "Journal date", journalDate.ToString("yyyy-MM-dd") }
                            }
                        );
                        return (false, true);
                    }

                    if (loop_counter > 10)
                    {
                        await SendAdminNotification(
                            discord,
                            "Failed to download journal",
                            "Encountered an error too many times",
                            new Dictionary<string, string>
                            {
                                { "Response code", res.code.ToString() },
                                { "Content", content },
                                { "User identifier", user.UserIdentifier.ToString() },
                                { "Journal date", journalDate.ToString("yyyy-MM-dd") }
                            }
                        );

                        return (false, false);
                    }

                    switch (res.code)
                    {
                        case HttpStatusCode.PartialContent:
                            Thread.Sleep(1000);
                            res = await GetJournalAsync(journalDate, user, db, hc, minioClient);
                            break;
                    }
                    loop_counter++;
                }
            }
            catch (TooManyOldJournalItemsException ex)
            {
                await SendAdminNotification(discord,
                    "**[JOURNAL]** Exception: Too many old journal items",
                    "The user somehow has duplicates of journals stored for a single date",
                    new Dictionary<string, string> {
                        { "Exception", ex.ToString() },
                        { "User identifier", user.UserIdentifier.ToString() }
                    }
                );
                return (false, true);
            }
            catch (Exception ex)
            {
                var errorMessage = ex.ToString() + "\n\n" + JsonSerializer.Serialize(user, new JsonSerializerOptions() { WriteIndented = true });

                await SendAdminNotification(discord,
                    "**[JOURNAL]** Unhandled exception while downloading journals",
                    errorMessage
                    );
                return (false, false);
            }

            return (true, false);
        }

        static async Task<(HttpStatusCode code, HttpResponseMessage message)> GetJournalAsync(DateTime journalDate, Shared.Models.User.Profile user, MSSQLDB db, HttpClient hc, MinioClient minioClient)
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

            var journalLineCount = journalContent.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

            var journalBytes = ZipManager.Zip(journalContent.Trim());
            string fileName = $"{user.UserIdentifier}/journal/{journalDate.Year}/{journalDate.Month.ToString().PadLeft(2, '0')}/{journalDate.Day.ToString().PadLeft(2, '0')}.journal";

            if (updateFileOnS3)
            {
                using (var ms = new MemoryStream(journalBytes))
                {
                    await minioClient.PutObjectAsync("journal-limpet", fileName, ms, ms.Length, "application/gzip");
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
    new SqlParameter("last_processed_line_number", journalLineCount),
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
    new SqlParameter("last_processed_line_number", journalLineCount),
    new SqlParameter("complete_entry", DateTime.UtcNow.Date > journalDate.Date)
);
            }

            await SSEActivitySender.SendUserActivityAsync(user.UserIdentifier,
                $"Downloaded journals for {journalDate:yyyy-MM-dd}",
                $"We downloaded {journalLineCount:N0} lines of journal for this day",
                "success"
            );

            await SSEActivitySender.SendStatsActivityAsync(db);

            Thread.Sleep(5000);
            return (HttpStatusCode.OK, journalRequest);
        }
    }
}

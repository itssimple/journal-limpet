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
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs
{
    public class EDDNUserUploader
    {
        public static async Task UploadAsync(Guid userIdentifier, PerformContext context)
        {
            using (var rlock = new RedisJobLock($"EDDNUserUploader.UploadAsync.{userIdentifier}"))
            {
                if (!rlock.TryTakeLock()) return;

                using (var scope = Startup.ServiceProvider.CreateScope())
                {
                    MSSQLDB db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();
                    IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                    MinioClient _minioClient = scope.ServiceProvider.GetRequiredService<MinioClient>();

                    IHttpClientFactory _hcf = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

                    var hc = _hcf.CreateClient();

                    var userJournals = await db.ExecuteListAsync<UserJournal>(
                        "SELECT * FROM user_journal WHERE user_identifier = @user_identifier AND sent_to_eddn = 0 AND last_processed_line_number > 0 ORDER BY journal_date ASC",
                            new SqlParameter("user_identifier", userIdentifier)
                        );

                    context.WriteLine($"Uploading journals for user {userIdentifier}");
                    string lastLine = string.Empty;

                    foreach (var journalItem in userJournals.WithProgress(context))
                    {

                        try
                        {
                            using (MemoryStream outFile = new MemoryStream())
                            {
                                string journalContent = string.Empty;

                                var stats = await _minioClient.StatObjectAsync("journal-limpet", journalItem.S3Path);

                                await _minioClient.GetObjectAsync("journal-limpet", journalItem.S3Path,
                                    0, stats.Size,
                                    cb =>
                                    {
                                        cb.CopyTo(outFile);
                                    }
                                );

                                outFile.Seek(0, SeekOrigin.Begin);
                                using (var sr = new StreamReader(outFile))
                                {
                                    journalContent = sr.ReadToEnd();
                                }

                                var journalRows = journalContent.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);

                                int line_number = journalItem.SentToEDDNLine;

                                int delay_time = 50;

                                foreach (var row in journalRows.Skip(line_number).WithProgress(context, journalItem.JournalDate.ToString("yyyy-MM-dd")))
                                {
                                    lastLine = row;
                                    var time = await UploadJournalItemToEDDN(hc, row, userIdentifier);

                                    if (time.TotalMilliseconds > 500)
                                    {
                                        delay_time = 500;
                                    }
                                    else if (time.TotalMilliseconds > 250)
                                    {
                                        delay_time = 250;
                                    }
                                    else if (time.TotalMilliseconds > 100)
                                    {
                                        delay_time = 100;
                                    }
                                    else if (time.TotalMilliseconds < 100)
                                    {
                                        delay_time = 50;
                                    }

                                    line_number++;
                                    await db.ExecuteNonQueryAsync(
                                        "UPDATE user_journal SET sent_to_eddn_line = @line_number WHERE journal_id = @journal_id",
                                        new SqlParameter("journal_id", journalItem.JournalId),
                                        new SqlParameter("line_number", line_number)
                                    );
                                    await Task.Delay(delay_time);
                                }

                                if (journalItem.CompleteEntry)
                                {
                                    await db.ExecuteNonQueryAsync(
                                        "UPDATE user_journal SET sent_to_eddn = 1, sent_to_eddn_line = @line_number WHERE journal_id = @journal_id",
                                        new SqlParameter("journal_id", journalItem.JournalId),
                                        new SqlParameter("line_number", line_number)
                                    );
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            await MailSender.SendSingleEmail(configuration, "no-reply+eddn@journal-limpet.com", "EDDN error", ex.ToString() + "\n\nUser:\n\n" + userIdentifier + "\n\nData:\n\n" + lastLine);
                        }
                    }
                }
            }
        }

        public enum AllowedEvents
        {
            Docked,
            FSDJump,
            Scan,
            Location,
            SAASignalsFound,
            CarrierJump
        }

        public enum RemoveJournalProperties
        {
            Wanted,
            ActiveFine,
            CockpitBreach,
            BoostUsed,
            FuelLevel,
            FuelUsed,
            JumpDist,
            Latitude,
            Longitude,
            HappiestSystem,
            HomeSystem,
            MyReputation,
            SquadronFaction
        }

        public enum RequiredProperties
        {
            StarSystem,
            StarPos,
            SystemAddress,
            timestamp,
            @event
        }

        internal static async Task<TimeSpan> UploadJournalItemToEDDN(HttpClient hc, string journalRow, Guid userIdentifier)
        {
            var element = JsonDocument.Parse(journalRow).RootElement;
            if (!element.TryGetProperty("event", out JsonElement journalEvent)) return TimeSpan.Zero;
            if (!System.Enum.TryParse(typeof(AllowedEvents), journalEvent.GetString(), false, out _)) return TimeSpan.Zero;

            element = FixEDDNJson(element);
            element = AddMissingProperties(element);

            if (HasMissingProperties(element)) return TimeSpan.Zero;

            var eddnItem = new Dictionary<string, object>()
    {
        { "$schemaRef", "https://eddn.edcd.io/schemas/journal/1" },
        { "header", new Dictionary<string, object>() {
            { "uploaderID", userIdentifier.ToString() },
            { "softwareName", "Journal Limpet" },
            { "softwareVersion", SharedSettings.VersionNumber }
        } },
        { "message", element }
    };

            var json = JsonSerializer.Serialize(eddnItem, new JsonSerializerOptions() { WriteIndented = true });

            Stopwatch sw = new Stopwatch();
            sw.Start();

            var policy = Policy
                .Handle<HttpRequestException>()
                .WaitAndRetryAsync(new[] {
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(4),
                    TimeSpan.FromSeconds(8),
                    TimeSpan.FromSeconds(16),
                });

            var status = await policy.ExecuteAsync(() => hc.PostAsync("https://eddn.edcd.io:4430/upload/", new StringContent(json, Encoding.UTF8, "application/json")));
            sw.Stop();

            var postResponse = await status.Content.ReadAsStringAsync();

            if (!status.IsSuccessStatusCode)
            {
                throw new Exception("EDDN exception: " + postResponse);
            }

            return sw.Elapsed;
        }

        internal static bool HasMissingProperties(JsonElement element)
        {
            var elementAsDictionary = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element.GetRawText());
            var requiredProperties = elementAsDictionary.Keys.Where(k => System.Enum.TryParse(typeof(RequiredProperties), k, false, out _));
            return requiredProperties.Count() < 5;
        }

        internal static JsonElement AddMissingProperties(JsonElement element)
        {
            var _rdb = SharedSettings.RedisClient.GetDatabase(1);

            var elementAsDictionary = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element.GetRawText());

            var reqProps = typeof(RequiredProperties).GetEnumNames();

            var requiredProperties = elementAsDictionary.Keys.Where(k => System.Enum.TryParse(typeof(RequiredProperties), k, false, out _));

            var missingProps = reqProps.Except(requiredProperties);

            if (!missingProps.Any())
            {
                _rdb.StringSet(
                    $"SystemAddress:{elementAsDictionary["SystemAddress"].GetInt64()}",
                    JsonSerializer.Serialize(new
                    {
                        SystemAddress = elementAsDictionary["SystemAddress"].GetInt64(),
                        StarSystem = elementAsDictionary["StarSystem"].GetString(),
                        StarPos = elementAsDictionary["StarPos"]
                    }),
                    TimeSpan.FromMinutes(10)
                );
                return element;
            }

            var importantProps = new[] { "StarPos", "StarSystem", "SystemAddress" };

            // We're missing all important props, just let it go and ignore the event
            if (importantProps.All(i => missingProps.Contains(i)))
            {
                return element;
            }

            if (!missingProps.Contains("SystemAddress"))
            {
                var cachedSystem = _rdb.StringGet($"SystemAddress:{elementAsDictionary["SystemAddress"].GetInt64()}");
                if (cachedSystem != RedisValue.Null)
                {
                    var jel = JsonDocument.Parse(cachedSystem.ToString()).RootElement;
                    elementAsDictionary["SystemAddress"] = jel.GetProperty("SystemAddress");
                    elementAsDictionary["StarSystem"] = jel.GetProperty("StarSystem");
                    elementAsDictionary["StarPos"] = jel.GetProperty("StarPos");
                }
            }

            var json = JsonSerializer.Serialize(elementAsDictionary);

            return JsonDocument.Parse(json).RootElement;
        }

        internal static JsonElement FixEDDNJson(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object && element.ValueKind != JsonValueKind.Array) return element;

            if (element.ValueKind == JsonValueKind.Array)
            {
                var elementAsList = JsonSerializer.Deserialize<List<JsonElement>>(element.GetRawText());

                for (var i = 0; i < elementAsList.Count; i++)
                {
                    var lElement = elementAsList[i];
                    elementAsList[i] = FixEDDNJson(lElement);
                }

                return JsonDocument.Parse(JsonSerializer.Serialize(elementAsList)).RootElement;
            }

            var elementAsDictionary = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element.GetRawText());

            var localisedProperties = elementAsDictionary.Keys.Where(k => k.EndsWith("_Localised"));
            foreach (var localised in localisedProperties)
            {
                elementAsDictionary.Remove(localised);
            }

            var removeProperties = elementAsDictionary.Keys.Where(k => System.Enum.TryParse(typeof(RemoveJournalProperties), k, false, out _));
            foreach (var remove in removeProperties)
            {
                elementAsDictionary.Remove(remove);
            }

            for (var i = 0; i < elementAsDictionary.Count; i++)
            {
                var key = elementAsDictionary.Keys.ElementAt(i);
                elementAsDictionary[key] = FixEDDNJson(elementAsDictionary[key]);
            }

            var json = JsonSerializer.Serialize(elementAsDictionary);

            return JsonDocument.Parse(json).RootElement;
        }
    }
}

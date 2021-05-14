using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Jobs.SharedCode;
using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Journal_Limpet.Shared.Models.Journal;
using Journal_Limpet.Shared.Models.User;
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
        [JobDisplayName("EDDN uploader for {1} ({0}")]
        public static async Task UploadAsync(Guid userIdentifier, string cmdrName, PerformContext context)
        {
            using (var rlock = new RedisJobLock($"EDDNUserUploader.UploadAsync.{userIdentifier}"))
            {
                if (!rlock.TryTakeLock()) return;

                using (var scope = Startup.ServiceProvider.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();
                    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                    var _minioClient = scope.ServiceProvider.GetRequiredService<MinioClient>();
                    var discordClient = scope.ServiceProvider.GetRequiredService<DiscordWebhook>();

                    var starSystemChecker = scope.ServiceProvider.GetRequiredService<StarSystemChecker>();

                    var hc = SharedSettings.GetHttpClient(scope);

                    var user = await db.ExecuteSingleRowAsync<Profile>(
                        "SELECT * FROM user_profile WHERE user_identifier = @user_identifier AND deleted = 0 AND send_to_eddn = 1",
                        new SqlParameter("user_identifier", userIdentifier)
                    );

                    if (user == null)
                        return;

                    var userJournals = await db.ExecuteListAsync<UserJournal>(
                        "SELECT * FROM user_journal WHERE user_identifier = @user_identifier AND sent_to_eddn = 0 AND last_processed_line_number > 0 ORDER BY journal_date ASC",
                            new SqlParameter("user_identifier", userIdentifier)
                        );

                    (EDGameState previousGameState, UserJournal lastJournal) = await GameStateHandler.LoadGameState(db, userIdentifier, userJournals, "EDDN", context);

                    context.WriteLine($"Uploading journals for user {userIdentifier}");
                    string lastLine = string.Empty;

                    foreach (var journalItem in userJournals.WithProgress(context))
                    {
                        IntegrationJournalData ijd = GameStateHandler.GetIntegrationJournalData(journalItem, lastJournal, "EDDN");

                        if (ijd.LastSentLineNumber != lastJournal.SentToEDDNLine)
                        {
                            ijd = new IntegrationJournalData
                            {
                                FullySent = false,
                                LastSentLineNumber = 0,
                                CurrentGameState = new EDGameState()
                            };
                        }

                        try
                        {
                            using (MemoryStream outFile = new MemoryStream())
                            {
                                var journalRows = await JournalLoader.LoadJournal(_minioClient, journalItem, outFile);

                                int line_number = journalItem.SentToEDDNLine;
                                int delay_time = 50;
                                var restOfTheLines = journalRows.Skip(line_number).ToList();

                                foreach (var row in restOfTheLines.WithProgress(context, journalItem.JournalDate.ToString("yyyy-MM-dd")))
                                {
                                    lastLine = row;
                                    try
                                    {
                                        if (!string.IsNullOrWhiteSpace(row))
                                        {
                                            var time = await UploadJournalItemToEDDN(hc, row, cmdrName, ijd.CurrentGameState, userIdentifier, starSystemChecker, discordClient);

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
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        if (ex.ToString().Contains("JsonReaderException"))
                                        {
                                            // Ignore rows we cannot parse
                                            context.WriteLine("Error");
                                            context.WriteLine(ex.ToString());
                                            context.WriteLine(row);
                                        }
                                        else
                                        {
                                            await discordClient.SendMessageAsync("**[EDDN Upload]** Unhandled exception", new List<DiscordWebhookEmbed>
                                            {
                                                new DiscordWebhookEmbed
                                                {
                                                    Description = "Unhandled exception",
                                                    Fields = new Dictionary<string, string>() {
                                                        { "User identifier", userIdentifier.ToString() },
                                                        { "Last line", lastLine },
                                                        { "Journal", journalItem.S3Path },
                                                        { "Exception", ex.ToString() },
                                                        { "Current GameState", JsonSerializer.Serialize(ijd.CurrentGameState, new JsonSerializerOptions { WriteIndented = true })}
                                                    }.Select(k => new DiscordWebhookEmbedField { Name = k.Key, Value = k.Value }).ToList()
                                                }
                                            });
                                            throw;
                                        }
                                    }

                                    line_number++;
                                    if (line_number <= journalItem.LastProcessedLineNumber)
                                    {
                                        ijd.LastSentLineNumber = line_number;
                                        journalItem.IntegrationData["EDDN"] = ijd;
                                    }

                                    await Task.Delay(delay_time);
                                }

                                var integration_json = JsonSerializer.Serialize(journalItem.IntegrationData);

                                await db.ExecuteNonQueryAsync(
                                    "UPDATE user_journal SET sent_to_eddn_line = @line_number, integration_data = @integration_data WHERE journal_id = @journal_id",
                                    new SqlParameter("journal_id", journalItem.JournalId),
                                    new SqlParameter("line_number", line_number),
                                    new SqlParameter("integration_data", integration_json)
                                );

                                if (journalItem.CompleteEntry)
                                {
                                    ijd.LastSentLineNumber = line_number;
                                    ijd.FullySent = true;
                                    journalItem.IntegrationData["EDDN"] = ijd;

                                    await db.ExecuteNonQueryAsync(
                                        "UPDATE user_journal SET sent_to_eddn = 1, sent_to_eddn_line = @line_number, integration_data = @integration_data WHERE journal_id = @journal_id",
                                        new SqlParameter("journal_id", journalItem.JournalId),
                                        new SqlParameter("line_number", line_number),
                                        new SqlParameter("integration_data", integration_json)
                                    );
                                }
                            }

                            lastJournal = journalItem;
                        }
                        catch (Exception ex)
                        {
                            await db.ExecuteNonQueryAsync(
                                "UPDATE user_journal SET integration_data = @integration_data WHERE journal_id = @journal_id",
                                new SqlParameter("journal_id", journalItem.JournalId),
                                new SqlParameter("integration_data", JsonSerializer.Serialize(journalItem.IntegrationData))
                            );

                            await discordClient.SendMessageAsync("**[EDDN Upload]** Problem with upload to EDDN", new List<DiscordWebhookEmbed>
                            {
                                new DiscordWebhookEmbed
                                {
                                    Description = ex.ToString(),
                                    Fields = new Dictionary<string, string>() {
                                        { "User identifier", userIdentifier.ToString() },
                                        { "Last line", lastLine },
                                        { "Journal", journalItem.S3Path }
                                    }.Select(k => new DiscordWebhookEmbedField { Name = k.Key, Value = k.Value }).ToList()
                                }
                            });
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

        internal static async Task<TimeSpan> UploadJournalItemToEDDN(HttpClient hc, string journalRow, string commander, EDGameState gameState, Guid userIdentifier, StarSystemChecker starSystemChecker, DiscordWebhook discordClient)
        {
            var element = JsonDocument.Parse(journalRow).RootElement;
            if (element.ValueKind != JsonValueKind.Object) return TimeSpan.Zero;

            if (!element.TryGetProperty("event", out JsonElement journalEvent)) return TimeSpan.Zero;

            var transientState = await SetGamestateProperties(element, gameState, commander, starSystemChecker);

            if (!System.Enum.TryParse(typeof(AllowedEvents), journalEvent.GetString(), false, out _)) return TimeSpan.Zero;

            element = FixEDDNJson(element);
            element = await AddMissingProperties(element, starSystemChecker, transientState.GetProperty("odyssey"));

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

            await SSEActivitySender.SendUserLogDataAsync(userIdentifier, new { fromIntegration = "EDDN", data = eddnItem });

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
                await discordClient.SendMessageAsync("**[EDDN Upload]** Problem with upload to EDDN", new List<DiscordWebhookEmbed>
                {
                    new DiscordWebhookEmbed
                    {
                        Description = "Got an error while posting data to EDDN",
                        Fields = new Dictionary<string, string>() {
                            { "User identifier", userIdentifier.ToString() },
                            { "Last line", journalRow },
                            { "JSON Sent", json }
                        }.Select(k => new DiscordWebhookEmbedField { Name = k.Key, Value = k.Value }).ToList()
                    }
                });

                throw new Exception("EDDN exception: " + postResponse);
            }

            return sw.Elapsed;
        }

        internal static bool HasMissingProperties(JsonElement element)
        {
            var elementAsDictionary = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element.GetRawText());
            var requiredProperties = elementAsDictionary.Keys.Where(k => System.Enum.TryParse(typeof(RequiredPropertiesForCache), k, false, out _));
            return requiredProperties.Count() < 5;
        }

        internal async static Task<JsonElement> AddMissingProperties(JsonElement element, StarSystemChecker starSystemChecker, JsonElement odyssey)
        {
            var _rdb = SharedSettings.RedisClient.GetDatabase(1);

            var elementAsDictionary = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element.GetRawText());

            elementAsDictionary["odyssey"] = odyssey;

            var reqProps = typeof(RequiredPropertiesForCache).GetEnumNames();

            var requiredProperties = elementAsDictionary.Keys.Where(k => System.Enum.TryParse(typeof(RequiredPropertiesForCache), k, false, out _));

            var missingProps = reqProps.Except(requiredProperties);

            if (!missingProps.Any())
            {
                await _rdb.StringSetAsyncWithRetries(
                    $"SystemAddress:{elementAsDictionary["SystemAddress"].GetInt64()}",
                    JsonSerializer.Serialize(new
                    {
                        SystemAddress = elementAsDictionary["SystemAddress"].GetInt64(),
                        StarSystem = elementAsDictionary["StarSystem"].GetString(),
                        StarPos = elementAsDictionary["StarPos"]
                    }),
                    TimeSpan.FromHours(10),
                    flags: CommandFlags.FireAndForget
                );

                var arrayEnum = elementAsDictionary["StarPos"].EnumerateArray().ToArray();

                var edSysData = new EDSystemData
                {
                    Id64 = elementAsDictionary["SystemAddress"].GetInt64(),
                    Name = elementAsDictionary["StarSystem"].GetString(),
                    Coordinates = new EDSystemCoordinates
                    {
                        X = arrayEnum[0].GetDouble(),
                        Y = arrayEnum[1].GetDouble(),
                        Z = arrayEnum[2].GetDouble()
                    }
                };

                await starSystemChecker.InsertOrUpdateSystemAsync(edSysData);

                return JsonDocument.Parse(JsonSerializer.Serialize(elementAsDictionary)).RootElement;
            }

            var importantProps = new[] { "StarPos", "StarSystem", "SystemAddress" };

            // We're missing all important props, just let it go and ignore the event
            if (importantProps.All(i => missingProps.Contains(i)))
            {
                return JsonDocument.Parse(JsonSerializer.Serialize(elementAsDictionary)).RootElement;
            }

            if (!missingProps.Contains("SystemAddress"))
            {
                var cachedSystem = await _rdb.StringGetAsyncWithRetries($"SystemAddress:{elementAsDictionary["SystemAddress"].GetInt64()}");
                if (cachedSystem != RedisValue.Null)
                {
                    var jel = JsonDocument.Parse(cachedSystem.ToString()).RootElement;
                    elementAsDictionary["SystemAddress"] = jel.GetProperty("SystemAddress");
                    elementAsDictionary["StarSystem"] = jel.GetProperty("StarSystem");
                    elementAsDictionary["StarPos"] = jel.GetProperty("StarPos");
                }
                else
                {
                    var systemData = await starSystemChecker.GetSystemDataAsync(elementAsDictionary["SystemAddress"].GetInt64());
                    if (systemData != null)
                    {
                        var jel = JsonDocument.Parse(JsonSerializer.Serialize(new
                        {
                            SystemAddress = systemData.Id64,
                            StarSystem = systemData.Name,
                            StarPos = new[] { systemData.Coordinates.X, systemData.Coordinates.Y, systemData.Coordinates.Z }
                        })).RootElement;

                        elementAsDictionary["SystemAddress"] = jel.GetProperty("SystemAddress");
                        elementAsDictionary["StarSystem"] = jel.GetProperty("StarSystem");
                        elementAsDictionary["StarPos"] = jel.GetProperty("StarPos");
                    }
                }
            }

            return JsonDocument.Parse(JsonSerializer.Serialize(elementAsDictionary)).RootElement;
        }

        public static async Task<JsonElement> SetGamestateProperties(JsonElement element, EDGameState gameState, string commander, StarSystemChecker starSystemChecker)
        {
            return await GameStateHandler.SetGamestateProperties(element, gameState, commander, starSystemChecker, (newState) =>
            {
                return new
                {
                    systemName = gameState.SystemName,
                    systemAddress = gameState.SystemAddress,
                    systemCoordinates = gameState.SystemCoordinates,
                    bodyName = gameState.BodyName,
                    bodyId = gameState.BodyId,
                    clientVersion = "Journal Limpet: " + SharedSettings.VersionNumber,
                    odyssey = gameState.Odyssey,
                    isBeta = false
                };
            });
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

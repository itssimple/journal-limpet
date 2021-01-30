using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
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
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs
{
    public static class CanonnRDUserUploader
    {
        public static async Task UploadAsync(Guid userIdentifier, string cmdrName, PerformContext context)
        {
            using (var rlock = new RedisJobLock($"CanonnRDUserUploader.UploadAsync.{userIdentifier}"))
            {
                if (!rlock.TryTakeLock()) return;

                using (var scope = Startup.ServiceProvider.CreateScope())
                {
                    MSSQLDB db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();
                    IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                    MinioClient _minioClient = scope.ServiceProvider.GetRequiredService<MinioClient>();
                    var discordClient = scope.ServiceProvider.GetRequiredService<DiscordWebhook>();

                    IHttpClientFactory _hcf = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

                    var hc = _hcf.CreateClient();

                    var user = await db.ExecuteSingleRowAsync<Profile>(
@"SELECT *
FROM user_profile up
WHERE up.user_identifier = @user_identifier
AND up.deleted = 0
AND ISNULL(JSON_VALUE(up.integration_settings, '$.""Canonn R\u0026D"".enabled'), 'false') = 'true'",
new SqlParameter("user_identifier", userIdentifier)
                    );

                    if (user == null)
                        return;

                    var canonnRDSettings = user.IntegrationSettings["Canonn R&D"].GetTypedObject<CanonnRDIntegrationSettings>();

                    if (!canonnRDSettings.Enabled)
                    {
                        return;
                    }

                    var userJournals = await db.ExecuteListAsync<UserJournal>(
                        "SELECT * FROM user_journal WHERE user_identifier = @user_identifier AND ISNULL(JSON_VALUE(integration_data, '$.\"Canonn R\\u0026D\".lastSentLineNumber'), '0') < last_processed_line_number AND ISNULL(JSON_VALUE(integration_data, '$.\"Canonn R\\u0026D\".fullySent'), 'false') = 'false' ORDER BY journal_date ASC",
                        new SqlParameter("user_identifier", userIdentifier)
                    );

                    context.WriteLine($"Found {userJournals.Count} journals to send to Canonn R&D!");

                    EDGameState previousGameState = null;

                    var firstAvailableGameState = userJournals.FirstOrDefault();
                    if (firstAvailableGameState != null)
                    {
                        var previousJournal = await db.ExecuteSingleRowAsync<UserJournal>(
                            "SELECT TOP 1 * FROM user_journal WHERE user_identifier = @user_identifier AND journal_id < @journal_id AND last_processed_line_number > 0 AND integration_data IS NOT NULL ORDER BY journal_date DESC",
                            new SqlParameter("user_identifier", userIdentifier),
                            new SqlParameter("journal_id", firstAvailableGameState.JournalId)
                        );

                        if (previousJournal != null && previousJournal.IntegrationData.ContainsKey("Canonn R&D"))
                        {
                            previousGameState = previousJournal.IntegrationData["Canonn R&D"].CurrentGameState;

                            context.WriteLine($"Found previous gamestate: {JsonSerializer.Serialize(previousGameState, new JsonSerializerOptions { WriteIndented = true })}");
                        }
                    }
                    string lastLine = string.Empty;

                    UserJournal lastJournal = null;

                    bool disableIntegration = false;

                    var _rdb = SharedSettings.RedisClient.GetDatabase(3);
                    var validCanonnEvents = await _rdb.StringGetAsyncWithRetriesSaveIfMissing("canonn:allowedEvents", 10, GetValidCanonnEvents);
                    var canonnEvents = JsonSerializer.Deserialize<List<CanonnEvent>>(validCanonnEvents).Select(e => e.Definition.Event).ToList();

                    foreach (var journalItem in userJournals.WithProgress(context))
                    {
                        IntegrationJournalData ijd;
                        if (journalItem.IntegrationData.ContainsKey("Canonn R&D"))
                        {
                            ijd = journalItem.IntegrationData["Canonn R&D"];
                        }
                        else
                        {
                            ijd = new IntegrationJournalData
                            {
                                FullySent = false,
                                LastSentLineNumber = 0,
                                CurrentGameState = lastJournal?.IntegrationData["Canonn R&D"].CurrentGameState ?? new EDGameState()
                            };
                        }

                        ijd.CurrentGameState.SendEvents = true;

                        try
                        {
                            using (MemoryStream outFile = new MemoryStream())
                            {
                                var stats = await _minioClient.StatObjectAsync("journal-limpet", journalItem.S3Path);

                                await _minioClient.GetObjectAsync("journal-limpet", journalItem.S3Path,
                                    0, stats.Size,
                                    cb =>
                                    {
                                        cb.CopyTo(outFile);
                                    }
                                );

                                outFile.Seek(0, SeekOrigin.Begin);

                                var journalContent = ZipManager.Unzip(outFile.ToArray());
                                var journalRows = journalContent.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);

                                int line_number = ijd.LastSentLineNumber;
                                int delay_time = 200;

                                var restOfTheLines = journalRows.Skip(line_number).ToList();

                                bool breakJournal = false;

                                foreach (var row in restOfTheLines.WithProgress(context, journalItem.JournalDate.ToString("yyyy-MM-dd")))
                                {
                                    lastLine = row;
                                    try
                                    {
                                        if (!string.IsNullOrWhiteSpace(row))
                                        {
                                            var res = await UploadJournalItemToCanonnRD(hc, row, userIdentifier, cmdrName, ijd.CurrentGameState, configuration, canonnEvents);

                                            switch (res.errorCode)
                                            {
                                                // This is an error from the server, stop working on journals now
                                                case -1:
                                                    breakJournal = true;
                                                    await discordClient.SendMessageAsync("**[Canonn R&D Upload]** Error code from API", new List<DiscordWebhookEmbed>
                                                    {
                                                        new DiscordWebhookEmbed
                                                        {
                                                            Description = res.resultContent,
                                                            Fields = new Dictionary<string, string>() {
                                                                { "User identifier", userIdentifier.ToString() },
                                                                { "Last line", lastLine },
                                                                { "Journal", journalItem.S3Path }
                                                            }.Select(k => new DiscordWebhookEmbedField { Name = k.Key, Value = k.Value }).ToList()
                                                        }
                                                    });
                                                    break;

                                                // These codes are OK
                                                case 200:
                                                    break;

                                                // We're sending too many requests at once, let's break out of the loop and wait until next batch
                                                case 429:
                                                    breakJournal = true;
                                                    context.WriteLine("We're sending stuff too quickly, breaking out of the loop");
                                                    context.WriteLine(res.resultContent);
                                                    await Task.Delay(30000);
                                                    break;

                                                // Exceptions and debug
                                                case 500: // Exception: %%
                                                case 501: // %%
                                                case 502: // Broken gateway
                                                case 503:
                                                    breakJournal = true;
                                                    context.WriteLine("We got an error from the service, breaking off!");
                                                    context.WriteLine(res.resultContent);
                                                    break;
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        if (ex.ToString().Contains("JsonReaderException"))
                                        {
                                            // Ignore rows we cannot parse
                                        }
                                        else
                                        {
                                            throw;
                                        }
                                    }

                                    if (breakJournal)
                                    {
                                        break;
                                    }

                                    line_number++;
                                    ijd.LastSentLineNumber = line_number;
                                    journalItem.IntegrationData["Canonn R&D"] = ijd;

                                    var integration_json = JsonSerializer.Serialize(journalItem.IntegrationData);

                                    await db.ExecuteNonQueryAsync(
                                        "UPDATE user_journal SET integration_data = @integration_data WHERE journal_id = @journal_id",
                                        new SqlParameter("journal_id", journalItem.JournalId),
                                        new SqlParameter("integration_data", integration_json)
                                    );
                                    await Task.Delay(delay_time);
                                }

                                if (breakJournal)
                                {
                                    context.WriteLine("We're breaking off here until next batch, we got told to do that.");
                                    break;
                                }

                                if (journalItem.CompleteEntry)
                                {
                                    ijd.LastSentLineNumber = line_number;
                                    ijd.FullySent = true;
                                    journalItem.IntegrationData["Canonn R&D"] = ijd;

                                    var integration_json_done = JsonSerializer.Serialize(journalItem.IntegrationData);

                                    await db.ExecuteNonQueryAsync(
                                        "UPDATE user_journal SET integration_data = @integration_data WHERE journal_id = @journal_id",
                                        new SqlParameter("journal_id", journalItem.JournalId),
                                        new SqlParameter("integration_data", integration_json_done)
                                    );
                                }
                            }
                            lastJournal = journalItem;
                        }
                        catch (Exception ex)
                        {
                            await discordClient.SendMessageAsync("**[Canonn R&D Upload]** Problem with upload to Canonn R&D", new List<DiscordWebhookEmbed>
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

                    if (disableIntegration)
                    {
                        canonnRDSettings.Enabled = false;
                        user.IntegrationSettings["Canonn R&D"] = canonnRDSettings.AsJsonElement();
                        var integrationJson = JsonSerializer.Serialize(user.IntegrationSettings);

                        await db.ExecuteNonQueryAsync(
                            "UPDATE user_profile SET integration_settings = @integration_settings WHERE user_identifier = @user_identifier",
                            new SqlParameter("user_identifier", userIdentifier),
                            new SqlParameter("integration_settings", integrationJson)
                        );
                    }
                }
            }
        }

        public enum CanonnRDEvents
        {
            CodexEntry
        }

        public static async Task<(int errorCode, string resultContent, TimeSpan executionTime)> UploadJournalItemToCanonnRD(HttpClient hc, string journalRow, Guid userIdentifier, string cmdrName, EDGameState gameState, IConfiguration configuration, List<string> validCanonnEvents)
        {
            var element = JsonDocument.Parse(journalRow).RootElement;
            if (!element.TryGetProperty("event", out JsonElement journalEvent)) return (303, string.Empty, TimeSpan.Zero);

            Stopwatch sw = new Stopwatch();
            sw.Start();

            var newGameState = await SetGamestateProperties(element, gameState, cmdrName);

            if (!validCanonnEvents.Contains(journalEvent.GetString())) return (304, string.Empty, TimeSpan.Zero);

            if (!gameState.SendEvents)
                return (200, string.Empty, TimeSpan.Zero);

            var eddnItem = new Dictionary<string, object>()
            {
                { "gameState", newGameState },
                { "rawEvent", element },
                { "eventType", journalEvent.GetString() },
                { "cmdrName", cmdrName }
            };

            var json = JsonSerializer.Serialize(eddnItem, new JsonSerializerOptions() { WriteIndented = true });

            var policy = Policy
                .Handle<HttpRequestException>()
                .WaitAndRetryAsync(new[] {
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(30),
                });

            var status = await policy.ExecuteAsync(() => hc.PostAsync(configuration["CanonnRD:JournalEndpoint"], new StringContent(json, Encoding.UTF8, "application/json")));

            var postResponseBytes = await status.Content.ReadAsByteArrayAsync();
            var postResponse = System.Text.Encoding.UTF8.GetString(postResponseBytes);

            if (!status.IsSuccessStatusCode)
            {
                return ((int)status.StatusCode, postResponse, TimeSpan.FromSeconds(30));
            }

            sw.Stop();

            return (200, postResponse, sw.Elapsed);
        }

        private async static Task<string> GetValidCanonnEvents()
        {
            using (var hc = new HttpClient())
            {
                return await hc.GetStringAsync("https://us-central1-canonn-api-236217.cloudfunctions.net/postEventWhitelist");
            }
        }

        public class CanonnEvent
        {
            [JsonPropertyName("description")]
            public string Description { get; set; }
            [JsonPropertyName("definition")]
            public CanonnEventDefinition Definition { get; set; }
        }

        public class CanonnEventDefinition
        {
            [JsonPropertyName("event")]
            public string Event { get; set; }
        }

        public enum RequiredPropertiesForCache
        {
            StarSystem,
            StarPos,
            SystemAddress,
            timestamp,
            @event
        }

        public static async Task<JsonElement> SetGamestateProperties(JsonElement element, EDGameState gameState, string commander)
        {
            var _rdb = SharedSettings.RedisClient.GetDatabase(1);
            var elementAsDictionary = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element.GetRawText());

            var reqProps = typeof(RequiredPropertiesForCache).GetEnumNames();

            var requiredProperties = elementAsDictionary.Keys.Where(k => System.Enum.TryParse(typeof(RequiredPropertiesForCache), k, false, out _));
            var missingProps = reqProps.Except(requiredProperties);

            bool setCache = false;

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

                await _rdb.StringSetAsyncWithRetries(
                    $"StarSystem:{elementAsDictionary["StarSystem"].GetString()}",
                    JsonSerializer.Serialize(new
                    {
                        SystemAddress = elementAsDictionary["SystemAddress"].GetInt64(),
                        StarSystem = elementAsDictionary["StarSystem"].GetString(),
                        StarPos = elementAsDictionary["StarPos"]
                    }),
                    TimeSpan.FromHours(10),
                    flags: CommandFlags.FireAndForget
                );

                setCache = true;
            }

            var importantProps = new[] { "StarPos", "StarSystem", "SystemAddress" };

            if (!missingProps.Contains("SystemAddress"))
            {
                var cachedSystem = await _rdb.StringGetAsyncWithRetries($"SystemAddress:{elementAsDictionary["SystemAddress"].GetInt64()}");
                if (cachedSystem != RedisValue.Null)
                {
                    var jel = JsonDocument.Parse(cachedSystem.ToString()).RootElement;
                    elementAsDictionary["SystemAddress"] = jel.GetProperty("SystemAddress");
                    elementAsDictionary["StarSystem"] = jel.GetProperty("StarSystem");
                    elementAsDictionary["StarPos"] = jel.GetProperty("StarPos");

                    setCache = true;
                }
            }
            else if (!missingProps.Contains("StarSystem"))
            {
                var cachedSystem = await _rdb.StringGetAsyncWithRetries($"StarSystem:{elementAsDictionary["StarSystem"].GetString()}");
                if (cachedSystem != RedisValue.Null)
                {
                    var jel = JsonDocument.Parse(cachedSystem.ToString()).RootElement;
                    elementAsDictionary["SystemAddress"] = jel.GetProperty("SystemAddress");
                    elementAsDictionary["StarSystem"] = jel.GetProperty("StarSystem");
                    elementAsDictionary["StarPos"] = jel.GetProperty("StarPos");

                    setCache = true;
                }
            }

            var eventName = elementAsDictionary["event"].GetString();
            var timestamp = elementAsDictionary["timestamp"].GetDateTimeOffset();

            gameState.Timestamp = timestamp;

            // We'll disable this reset for now, since commanders do re-log at times
            // And for some reason, Location wasn't written to the journals,
            // maybe it only appears if you actually restart the entire game?

            /*if (eventName == "LoadGame")
            {
                gameState.SystemAddress = null;
                gameState.SystemName = null;
                gameState.SystemCoordinates = null;
                gameState.MarketId = null;
                gameState.StationName = null;
                gameState.ShipId = null;
                gameState.BodyId = null;
                gameState.BodyName = null;
            }*/

            if (eventName == "SetUserShipName")
            {
                gameState.ShipId = elementAsDictionary["ShipID"].GetInt64();
            }

            if (eventName == "ShipyardBuy")
            {
                gameState.ShipId = null;
            }

            if (eventName == "ShipyardSwap")
            {
                gameState.ShipId = elementAsDictionary["ShipID"].GetInt64();
            }

            if (eventName == "Loadout")
            {
                gameState.ShipId = elementAsDictionary["ShipID"].GetInt64();
            }

            if (eventName == "Undocked")
            {
                gameState.MarketId = null;
                gameState.StationName = null;
            }

            if (eventName == "ApproachBody")
            {
                gameState.SystemName = elementAsDictionary["StarSystem"].GetString();
                gameState.SystemAddress = elementAsDictionary["SystemAddress"].GetInt64();
                gameState.BodyId = elementAsDictionary["BodyID"].GetInt64();
                gameState.BodyName = elementAsDictionary["Body"].GetString();
            }

            if (eventName == "LeaveBody")
            {
                gameState.BodyId = null;
                gameState.BodyName = null;
            }

            if (eventName == "SuperCruiseEntry")
            {
                gameState.SystemName = elementAsDictionary["StarSystem"].GetString();

                gameState.BodyName = null;
                gameState.BodyId = null;
            }

            if (eventName == "SuperCruiseExit")
            {
                gameState.SystemName = elementAsDictionary["StarSystem"].GetString();

                if (elementAsDictionary.ContainsKey("Body"))
                {
                    gameState.BodyName = elementAsDictionary["Body"].GetString();
                }
                else
                {
                    gameState.BodyName = null;
                }

                if (elementAsDictionary.ContainsKey("BodyID"))
                {
                    gameState.BodyId = elementAsDictionary["BodyID"].GetInt64();
                }
                else
                {
                    gameState.BodyId = null;
                }
            }

            if (new[] { "Location", "FSDJump", "Docked", "CarrierJump" }.Contains(eventName))
            {
                // Docked don"t have coordinates, if system changed reset
                if (elementAsDictionary["StarSystem"].GetString() != gameState.SystemName)
                {
                    gameState.SystemCoordinates = null;
                    gameState.SystemAddress = null;
                    gameState.BodyId = null;
                    gameState.BodyName = null;
                }

                if (elementAsDictionary["StarSystem"].GetString() != "ProvingGround" && elementAsDictionary["StarSystem"].GetString() != "CQC")
                {
                    if (elementAsDictionary.ContainsKey("SystemAddress"))
                    {
                        gameState.SystemAddress = elementAsDictionary["SystemAddress"].GetInt64();
                    }

                    gameState.SystemName = elementAsDictionary["StarSystem"].GetString();

                    if (elementAsDictionary.ContainsKey("StarPos"))
                    {
                        gameState.SystemCoordinates = elementAsDictionary["StarPos"];
                    }

                    if (elementAsDictionary.ContainsKey("Body"))
                    {
                        gameState.BodyName = elementAsDictionary["Body"].GetString();
                    }

                    if (elementAsDictionary.ContainsKey("BodyID"))
                    {
                        gameState.BodyId = elementAsDictionary["BodyID"].GetInt64();
                    }
                }
                else
                {
                    gameState.SystemAddress = null;
                    gameState.SystemName = null;
                    gameState.SystemCoordinates = null;
                    gameState.BodyId = null;
                    gameState.BodyName = null;
                }

                if (elementAsDictionary.ContainsKey("MarketID"))
                {
                    gameState.MarketId = elementAsDictionary["MarketID"].GetInt64();
                }
                if (elementAsDictionary.ContainsKey("StationName"))
                {
                    gameState.StationName = elementAsDictionary["StationName"].GetString();
                }
            }

            if (new[] { "JoinACrew", "QuitACrew" }.Contains(eventName))
            {
                if (eventName == "JoinACrew" && elementAsDictionary["Captain"].GetString() != commander)
                {
                    gameState.SendEvents = false;
                }

                else
                {
                    gameState.SendEvents = true;
                }

                gameState.SystemAddress = null;
                gameState.SystemName = null;
                gameState.SystemCoordinates = null;
                gameState.MarketId = null;
                gameState.StationName = null;
                gameState.ShipId = null;
                gameState.BodyId = null;
                gameState.BodyName = null;
            }

            var addItems = new
            {
                systemName = gameState.SystemName,
                systemAddress = gameState.SystemAddress,
                systemCoordinates = gameState.SystemCoordinates,
                bodyName = gameState.BodyName,
                bodyId = gameState.BodyId,
                clientVersion = "Journal Limpet: " + SharedSettings.VersionNumber,
                isBeta = false
            };

            var transientState = JsonDocument.Parse(JsonSerializer.Serialize(addItems)).RootElement;

            if (!setCache && gameState.SystemAddress.HasValue && gameState.SystemCoordinates.HasValue && !string.IsNullOrWhiteSpace(gameState.SystemName))
            {
                await _rdb.StringSetAsyncWithRetries(
                    $"SystemAddress:{gameState.SystemAddress}",
                    JsonSerializer.Serialize(new
                    {
                        SystemAddress = gameState.SystemAddress,
                        StarSystem = gameState.SystemName,
                        StarPos = gameState.SystemCoordinates
                    }),
                    TimeSpan.FromHours(10),
                    flags: CommandFlags.FireAndForget
                );

                await _rdb.StringSetAsyncWithRetries(
                    $"StarSystem:{gameState.SystemName}",
                    JsonSerializer.Serialize(new
                    {
                        SystemAddress = gameState.SystemAddress,
                        StarSystem = gameState.SystemName,
                        StarPos = gameState.SystemCoordinates
                    }),
                    TimeSpan.FromHours(10),
                    flags: CommandFlags.FireAndForget
                );
            }

            return transientState;
        }
    }
}

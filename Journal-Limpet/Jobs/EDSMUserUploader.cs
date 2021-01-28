using Hangfire.Console;
using Hangfire.Server;
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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs
{
    public static class EDSMUserUploader
    {
        public static async Task UploadAsync(Guid userIdentifier, PerformContext context)
        {
            using (var rlock = new RedisJobLock($"EDSMUserUploader.UploadAsync.{userIdentifier}"))
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
AND ISNULL(JSON_VALUE(up.integration_settings, '$.EDSM.enabled'), 'false') = 'true'
AND JSON_VALUE(up.integration_settings, '$.EDSM.apiKey') IS NOT NULL
AND JSON_VALUE(up.integration_settings, '$.EDSM.cmdrName') IS NOT NULL",
new SqlParameter("user_identifier", userIdentifier)
                    );

                    if (user == null)
                        return;

                    var edsmSettings = user.IntegrationSettings["EDSM"].GetTypedObject<EDSMIntegrationSettings>();

                    if (!edsmSettings.Enabled || string.IsNullOrWhiteSpace(edsmSettings.CommanderName) || string.IsNullOrWhiteSpace(edsmSettings.ApiKey))
                    {
                        return;
                    }

                    var userJournals = await db.ExecuteListAsync<UserJournal>(
                        "SELECT * FROM user_journal WHERE user_identifier = @user_identifier AND ISNULL(JSON_VALUE(integration_data, '$.EDSM.lastSEntLineNumber'), '0') < last_processed_line_number AND ISNULL(JSON_VALUE(integration_data, '$.EDSM.fullySent'), 'false') = 'false' ORDER BY journal_date ASC",
                        new SqlParameter("user_identifier", userIdentifier)
                    );

                    context.WriteLine($"Found {userJournals.Count} to send to EDSM!");

                    EDGameState previousGameState = null;

                    var firstAvailableGameState = userJournals.FirstOrDefault();
                    if (firstAvailableGameState != null)
                    {
                        var previousJournal = await db.ExecuteSingleRowAsync<UserJournal>(
                            "SELECT TOP 1 * FROM user_journal WHERE user_identifier = @user_identifier AND journal_id < @journal_id AND last_processed_line_number > 0 AND integration_data IS NOT NULL ORDER BY journal_date DESC",
                            new SqlParameter("user_identifier", userIdentifier),
                            new SqlParameter("journal_id", firstAvailableGameState.JournalId)
                        );

                        if (previousJournal != null && previousJournal.IntegrationData.ContainsKey("EDSM"))
                        {
                            previousGameState = previousJournal.IntegrationData["EDSM"].CurrentGameState;

                            context.WriteLine($"Found previous gamestate: {JsonSerializer.Serialize(previousGameState, new JsonSerializerOptions { WriteIndented = true })}");
                        }
                    }
                    string lastLine = string.Empty;

                    UserJournal lastJournal = null;

                    bool disableIntegration = false;

                    foreach (var journalItem in userJournals.WithProgress(context))
                    {
                        IntegrationJournalData ijd;
                        if (journalItem.IntegrationData.ContainsKey("EDSM"))
                        {
                            ijd = journalItem.IntegrationData["EDSM"];
                        }
                        else
                        {
                            ijd = new IntegrationJournalData
                            {
                                FullySent = false,
                                LastSentLineNumber = 0,
                                CurrentGameState = lastJournal?.IntegrationData["EDSM"].CurrentGameState ?? new EDGameState()
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
                                            var res = await UploadJournalItemToEDSM(hc, row, userIdentifier, edsmSettings, ijd.CurrentGameState);

                                            switch (res.errorCode)
                                            {
                                                // This is an error from the server, stop working on journals now
                                                case -1:
                                                    breakJournal = true;
                                                    await discordClient.SendMessageAsync("**[EDSM Upload]** Error code from API", new List<DiscordWebhookEmbed>
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
                                                case 100: // OK
                                                case 101: // Message already stored
                                                case 102: // Message older than the stored one
                                                case 103: // Duplicate event request
                                                case 104: // Crew session
                                                    break;

                                                // For these codes we break
                                                case 201: // Missing commander name
                                                case 202: // Missing API key
                                                case 203: // Commander name/API Key not found
                                                case 204: // Software/Software version not found
                                                case 205: // Blacklisted software
                                                    disableIntegration = true;
                                                    breakJournal = true;
                                                    break;
                                                case 206: // Cannot decode JSON
                                                    break;

                                                // Missing/broken things, just ignore and go ahead with the rest of the import
                                                case 301: // Message not found
                                                case 302: // Cannot decode message JSON
                                                case 303: // Missing timestamp/event from message
                                                case 304: // Discarded event
                                                    break;

                                                // Other errors.. just go ahead, pretend nothing happened
                                                case 401: // Category unknown
                                                case 402: // Item unknown
                                                case 451: // System probably non existant
                                                case 452: // An entry for the same system already exists just before the visited date.
                                                case 453: // An entry for the same system already exists just after the visited date.
                                                    break;

                                                // Exceptions and debug
                                                case 500: // Exception: %%
                                                case 501: // %%
                                                case 502: // Broken gateway
                                                    breakJournal = true;
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
                                    journalItem.IntegrationData["EDSM"] = ijd;

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
                                    break;
                                }

                                if (journalItem.CompleteEntry)
                                {
                                    ijd.LastSentLineNumber = line_number;
                                    ijd.FullySent = true;
                                    journalItem.IntegrationData["EDSM"] = ijd;

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
                            await discordClient.SendMessageAsync("**[EDSM Upload]** Problem with upload to EDSM", new List<DiscordWebhookEmbed>
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
                        edsmSettings.Enabled = false;
                        user.IntegrationSettings["EDSM"] = edsmSettings.AsJsonElement();
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

        public enum IgnoredEvents
        {
            JournalLimpetFileheader,
            AfmuRepairs,
            AppliedToSquadron,
            ApproachBody,
            AsteroidCracked,
            Bounty,
            CapShipBond,
            CargoTransfer,
            CarrierBankTransfer,
            CarrierBuy,
            CarrierCrewServices,
            CarrierDecommission,
            CarrierDepositFuel,
            CarrierDockingPermission,
            CarrierFinance,
            CarrierJumpCancelled,
            CarrierJumpRequest,
            CarrierModulePack,
            CarrierNameChange,
            CarrierStats,
            CarrierTradeOrder,
            ChangeCrewRole,
            ClearSavedGame,
            CockpitBreached,
            Commander,
            Continued,
            Coriolis,
            CrewAssign,
            CrewFire,
            CrewLaunchFighter,
            CrewMemberJoins,
            CrewMemberQuits,
            CrewMemberRoleChange,
            CrimeVictim,
            DataScanned,
            DatalinkScan,
            DatalinkVoucher,
            DisbandedSquadron,
            DiscoveryScan,
            DockFighter,
            DockSRV,
            DockingCancelled,
            DockingDenied,
            DockingGranted,
            DockingRequested,
            DockingTimeout,
            EDDCommodityPrices,
            EDDItemSet,
            EDShipyard,
            EndCrewSession,
            EngineerApply,
            EngineerLegacyConvert,
            EscapeInterdiction,
            FSSSignalDiscovered,
            FactionKillBond,
            FighterDestroyed,
            FighterRebuilt,
            Fileheader,
            FuelScoop,
            HeatDamage,
            HeatWarning,
            HullDamage,
            InvitedToSquadron,
            JetConeBoost,
            JetConeDamage,
            JoinedSquadron,
            KickCrewMember,
            LaunchDrone,
            LaunchFighter,
            LaunchSRV,
            LeaveBody,
            LeftSquadron,
            Liftoff,
            Market,
            MassModuleStore,
            MaterialDiscovered,
            ModuleArrived,
            ModuleInfo,
            ModuleStore,
            ModuleSwap,
            Music,
            NavBeaconScan,
            NavRoute,
            NewCommander,
            NpcCrewRank,
            Outfitting,
            PVPKill,
            Passengers,
            PowerplayVote,
            PowerplayVoucher,
            ProspectedAsteroid,
            RebootRepair,
            ReceiveText,
            RepairDrone,
            ReservoirReplenished,
            SRVDestroyed,
            Scanned,
            Screenshot,
            SendText,
            SharedBookmarkToSquadron,
            ShieldState,
            ShipArrived,
            ShipTargeted,
            Shipyard,
            ShipyardNew,
            ShutDown,
            Shutdown,
            SquadronCreated,
            SquadronStartup,
            StartJump,
            Status,
            StoredModules,
            SupercruiseEntry,
            SupercruiseExit,
            SystemsShutdown,
            Touchdown,
            UnderAttack,
            VehicleSwitch,
            WingAdd,
            WingInvite,
            WingJoin,
            WingLeave
        }

        public static async Task<(int errorCode, string resultContent, TimeSpan executionTime)> UploadJournalItemToEDSM(HttpClient hc, string journalRow, Guid userIdentifier, EDSMIntegrationSettings edsmSettings, EDGameState gameState)
        {
            var element = JsonDocument.Parse(journalRow).RootElement;
            if (!element.TryGetProperty("event", out JsonElement journalEvent)) return (303, string.Empty, TimeSpan.Zero);

            Stopwatch sw = new Stopwatch();
            sw.Start();

            element = await SetGamestateProperties(element, gameState, edsmSettings.CommanderName);

            if (System.Enum.TryParse(typeof(IgnoredEvents), journalEvent.GetString(), false, out _)) return (304, string.Empty, TimeSpan.Zero);

            if (!gameState.SendEvents)
                return (104, string.Empty, TimeSpan.Zero);

            var formContent = new MultipartFormDataContent();

            var json = JsonSerializer.Serialize(element, new JsonSerializerOptions() { WriteIndented = true });

            formContent.Add(new StringContent(edsmSettings.CommanderName), "commanderName");
            formContent.Add(new StringContent(edsmSettings.ApiKey), "apiKey");
            formContent.Add(new StringContent("Journal Limpet"), "fromSoftware");
            formContent.Add(new StringContent(SharedSettings.VersionNumber), "fromSoftwareVersion");
            formContent.Add(new StringContent(json), "message");

            var policy = Policy
                .Handle<HttpRequestException>()
                .WaitAndRetryAsync(new[] {
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(4),
                    TimeSpan.FromSeconds(8),
                    TimeSpan.FromSeconds(16),
                });

            var status = await policy.ExecuteAsync(() => hc.PostAsync("https://www.edsm.net/api-journal-v1", formContent));
            var postResponseBytes = await status.Content.ReadAsByteArrayAsync();

            var postResponse = System.Text.Encoding.UTF8.GetString(postResponseBytes);
            if (!status.IsSuccessStatusCode)
            {
                return ((int)status.StatusCode, postResponse, TimeSpan.FromSeconds(30));
            }

            var resp = JsonSerializer.Deserialize<EDSMApiResponse>(postResponse);

            sw.Stop();

            return (resp.ResultCode, postResponse, sw.Elapsed);
        }

        public class EDSMApiResponse : EliteBaseJsonObject
        {
            [JsonPropertyName("msgnum")]
            public int ResultCode { get; set; }
            [JsonPropertyName("msg")]
            public string Message { get; set; }
            [JsonPropertyName("events")]
            public List<EDSMApiResponse> Events { get; set; }
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

            if (eventName == "LoadGame")
            {
                gameState.SystemAddress = null;
                gameState.SystemName = null;
                gameState.SystemCoordinates = null;
                gameState.MarketId = null;
                gameState.StationName = null;
                gameState.ShipId = null;
            }

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

            if (new[] { "Location", "FSDJump", "Docked" }.Contains(eventName))
            {
                // Docked don"t have coordinates, if system changed reset
                if (elementAsDictionary["StarSystem"].GetString() != gameState.SystemName)
                {
                    gameState.SystemCoordinates = null;
                    gameState.SystemAddress = null;
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
                }
                else
                {
                    gameState.SystemAddress = null;
                    gameState.SystemName = null;
                    gameState.SystemCoordinates = null;
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
            }

            var addItems = new
            {
                _systemAddress = gameState.SystemAddress,
                _systemName = gameState.SystemName,
                _systemCoordinates = gameState.SystemCoordinates,
                _marketId = gameState.MarketId,
                _stationName = gameState.StationName,
                _shipId = gameState.ShipId,
            };

            var transientState = JsonDocument.Parse(JsonSerializer.Serialize(addItems)).RootElement;

            elementAsDictionary["_systemAddress"] = transientState.GetProperty("_systemAddress");
            elementAsDictionary["_systemName"] = transientState.GetProperty("_systemName");
            elementAsDictionary["_systemCoordinates"] = transientState.GetProperty("_systemCoordinates");
            elementAsDictionary["_marketId"] = transientState.GetProperty("_marketId");
            elementAsDictionary["_stationName"] = transientState.GetProperty("_stationName");
            elementAsDictionary["_shipId"] = transientState.GetProperty("_shipId");

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

            var json = JsonSerializer.Serialize(elementAsDictionary);
            return JsonDocument.Parse(json).RootElement;
        }
    }
}

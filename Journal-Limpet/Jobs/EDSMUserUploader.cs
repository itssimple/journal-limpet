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
        [JobDisplayName("EDSM uploader for {0}")]
        public static async Task UploadAsync(Guid userIdentifier, PerformContext context)
        {
            using (var rlock = new RedisJobLock($"EDSMUserUploader.UploadAsync.{userIdentifier}"))
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
                        "SELECT * FROM user_journal WHERE user_identifier = @user_identifier AND ISNULL(JSON_VALUE(integration_data, '$.EDSM.lastSentLineNumber'), '0') < last_processed_line_number AND last_processed_line_number > 0 AND ISNULL(JSON_VALUE(integration_data, '$.EDSM.fullySent'), 'false') = 'false' ORDER BY journal_date ASC",
                        new SqlParameter("user_identifier", userIdentifier)
                    );

                    context.WriteLine($"Found {userJournals.Count} to send to EDSM!");

                    (EDGameState previousGameState, UserJournal lastJournal) = await GameStateHandler.LoadGameState(db, userIdentifier, userJournals, "EDSM", context);

                    string lastLine = string.Empty;

                    bool disableIntegration = false;

                    foreach (var journalItem in userJournals.WithProgress(context))
                    {
                        IntegrationJournalData ijd = GameStateHandler.GetIntegrationJournalData(journalItem, lastJournal, "EDSM");

                        try
                        {
                            using (MemoryStream outFile = new MemoryStream())
                            {
                                var journalRows = await JournalLoader.LoadJournal(_minioClient, journalItem, outFile);

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
                                            var res = await UploadJournalItemToEDSM(hc, row, userIdentifier, edsmSettings, ijd.CurrentGameState, starSystemChecker);


                                            if (res.sentData)
                                            {
                                                delay_time = 1000;
                                            }
                                            else
                                            {
                                                delay_time = 1;
                                            }

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
                                                    await discordClient.SendMessageAsync("**[EDSM Upload]** Disabled integration for user", new List<DiscordWebhookEmbed>
                                                    {
                                                        new DiscordWebhookEmbed
                                                        {
                                                            Description = res.resultContent,
                                                            Fields = new Dictionary<string, string>() {
                                                                { "Status code", res.errorCode.ToString() },
                                                                { "User identifier", userIdentifier.ToString() },
                                                                { "Last line", lastLine },
                                                                { "Journal", journalItem.S3Path }
                                                            }.Select(k => new DiscordWebhookEmbedField { Name = k.Key, Value = k.Value }).ToList()
                                                        }
                                                    });
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
                                            context.WriteLine(lastLine);
                                        }
                                        else
                                        {
                                            context.WriteLine("Unhandled exception");
                                            context.WriteLine(lastLine);

                                            await discordClient.SendMessageAsync("**[EDSM Upload]** Unhandled exception", new List<DiscordWebhookEmbed>
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

                                    if (breakJournal)
                                    {
                                        context.WriteLine("Jumping out from the journal");
                                        context.WriteLine(lastLine);
                                        break;
                                    }

                                    line_number++;
                                    ijd.LastSentLineNumber = line_number;
                                    journalItem.IntegrationData["EDSM"] = ijd;

                                    await Task.Delay(delay_time);
                                }

                                await GameStateHandler.UpdateJournalIntegrationDataAsync(db, journalItem.JournalId, IntegrationNames.EDSM, ijd);

                                //await db.ExecuteNonQueryAsync(
                                //    "UPDATE user_journal SET integration_data = @integration_data WHERE journal_id = @journal_id",
                                //    new SqlParameter("journal_id", journalItem.JournalId),
                                //    new SqlParameter("integration_data", JsonSerializer.Serialize(journalItem.IntegrationData))
                                //);

                                if (breakJournal)
                                {
                                    context.WriteLine("We're breaking off here until next batch, we got told to do that.");
                                    context.WriteLine(lastLine);
                                    break;
                                }

                                if (journalItem.CompleteEntry)
                                {
                                    ijd.LastSentLineNumber = line_number;
                                    ijd.FullySent = true;
                                    journalItem.IntegrationData["EDSM"] = ijd;

                                    await GameStateHandler.UpdateJournalIntegrationDataAsync(db, journalItem.JournalId, IntegrationNames.EDSM, ijd);

                                    //await db.ExecuteNonQueryAsync(
                                    //    "UPDATE user_journal SET integration_data = @integration_data WHERE journal_id = @journal_id",
                                    //    new SqlParameter("journal_id", journalItem.JournalId),
                                    //    new SqlParameter("integration_data", JsonSerializer.Serialize(journalItem.IntegrationData))
                                    //);
                                }
                            }
                            lastJournal = journalItem;
                        }
                        catch (Exception ex)
                        {
                            await GameStateHandler.UpdateJournalIntegrationDataAsync(db, journalItem.JournalId, IntegrationNames.EDSM, ijd);

                            //await db.ExecuteNonQueryAsync(
                            //    "UPDATE user_journal SET integration_data = @integration_data WHERE journal_id = @journal_id",
                            //    new SqlParameter("journal_id", journalItem.JournalId),
                            //    new SqlParameter("integration_data", JsonSerializer.Serialize(journalItem.IntegrationData))
                            //);

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

                        await db.ExecuteNonQueryAsync(
                            "UPDATE user_profile SET integration_settings = @integration_settings WHERE user_identifier = @user_identifier",
                            new SqlParameter("user_identifier", userIdentifier),
                            new SqlParameter("integration_settings", JsonSerializer.Serialize(user.IntegrationSettings))
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

        public static async Task<(int errorCode, string resultContent, TimeSpan executionTime, bool sentData)> UploadJournalItemToEDSM(HttpClient hc, string journalRow, Guid userIdentifier, EDSMIntegrationSettings edsmSettings, EDGameState gameState, StarSystemChecker starSystemChecker)
        {
            var element = JsonDocument.Parse(journalRow).RootElement;
            if (!element.TryGetProperty("event", out JsonElement journalEvent)) return (303, string.Empty, TimeSpan.Zero, false);

            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                element = await SetGamestateProperties(element, gameState, edsmSettings.CommanderName.Trim(), starSystemChecker);

                if (System.Enum.TryParse(typeof(IgnoredEvents), journalEvent.GetString(), false, out _)) return (304, string.Empty, TimeSpan.Zero, false);

                if (!gameState.SendEvents)
                    return (104, string.Empty, TimeSpan.Zero, false);

                var formContent = new MultipartFormDataContent();

                var json = JsonSerializer.Serialize(element, new JsonSerializerOptions() { WriteIndented = true });

                formContent.Add(new StringContent(edsmSettings.CommanderName.Trim()), "commanderName");
                formContent.Add(new StringContent(edsmSettings.ApiKey), "apiKey");
                formContent.Add(new StringContent("Journal Limpet"), "fromSoftware");
                formContent.Add(new StringContent(gameState.GameVersion), "fromGameVersion");
                formContent.Add(new StringContent(gameState.GameBuild), "fromGameBuild");
                formContent.Add(new StringContent(SharedSettings.VersionNumber), "fromSoftwareVersion");
                formContent.Add(new StringContent(json), "message");

                await SSEActivitySender.SendUserLogDataAsync(userIdentifier, new { fromIntegration = "EDSM", data = element });

                var policy = Policy
                    .HandleResult<HttpResponseMessage>(res => !res.IsSuccessStatusCode)
                    .Or<HttpRequestException>()
                    .WaitAndRetryAsync(30, retryCount => TimeSpan.FromSeconds(retryCount * 5));

                var status = await policy.ExecuteAsync(() => hc.PostAsync("https://www.edsm.net/api-journal-v1", formContent));
                var postResponseBytes = await status.Content.ReadAsByteArrayAsync();

                var postResponse = System.Text.Encoding.UTF8.GetString(postResponseBytes);
                if (!status.IsSuccessStatusCode)
                {
                    return ((int)status.StatusCode, postResponse, TimeSpan.FromSeconds(30), true);
                }

                var resp = JsonSerializer.Deserialize<EDSMApiResponse>(postResponse);

                sw.Stop();

                return (resp.ResultCode, postResponse, sw.Elapsed, true);
            }
            catch (InvalidTimestampException)
            {
                return (206, string.Empty, TimeSpan.FromMilliseconds(100), false);
            }
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

        public static async Task<JsonElement> SetGamestateProperties(JsonElement element, EDGameState gameState, string commander, StarSystemChecker starSystemChecker)
        {
            return await GameStateHandler.SetGamestateProperties(element, gameState, commander, starSystemChecker,
                (newState) => new
                {
                    _systemAddress = gameState.SystemAddress,
                    _systemName = gameState.SystemName,
                    _systemCoordinates = gameState.SystemCoordinates,
                    _marketId = gameState.MarketId,
                    _stationName = gameState.StationName,
                    _shipId = gameState.ShipId,
                    _odyssey = gameState.Odyssey
                },
                (transientState, elementAsDictionary) =>
                {
                    elementAsDictionary["_systemAddress"] = transientState.GetProperty("_systemAddress");
                    elementAsDictionary["_systemName"] = transientState.GetProperty("_systemName");
                    elementAsDictionary["_systemCoordinates"] = transientState.GetProperty("_systemCoordinates");
                    elementAsDictionary["_marketId"] = transientState.GetProperty("_marketId");
                    elementAsDictionary["_stationName"] = transientState.GetProperty("_stationName");
                    elementAsDictionary["_shipId"] = transientState.GetProperty("_shipId");
                }
            );
        }
    }
}

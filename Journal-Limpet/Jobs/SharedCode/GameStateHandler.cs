using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Journal_Limpet.Shared.Models.Journal;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs.SharedCode
{
    public static class GameStateHandler
    {
        public static async Task<(EDGameState gameState, UserJournal lastJournal)> LoadGameState(MSSQLDB db, Guid userIdentifier, List<UserJournal> userJournals, string integrationKey, PerformContext context)
        {
            EDGameState previousGameState = null;
            UserJournal lastJournal = null;

            var firstAvailableGameState = userJournals.FirstOrDefault();
            if (firstAvailableGameState != null)
            {
                lastJournal = await db.ExecuteSingleRowAsync<UserJournal>(
                    "SELECT TOP 1 * FROM user_journal WHERE user_identifier = @user_identifier AND journal_id <= @journal_id AND last_processed_line_number > 0 AND integration_data IS NOT NULL ORDER BY journal_date DESC",
                    new SqlParameter("user_identifier", userIdentifier),
                    new SqlParameter("journal_id", firstAvailableGameState.JournalId)
                );

                if (lastJournal != null && lastJournal.IntegrationData.ContainsKey(integrationKey))
                {
                    previousGameState = lastJournal.IntegrationData[integrationKey].CurrentGameState;

                    context.WriteLine($"Found previous gamestate: {JsonSerializer.Serialize(previousGameState, new JsonSerializerOptions { WriteIndented = true })}");
                }
            }

            return (previousGameState, lastJournal);
        }

        public static IntegrationJournalData GetIntegrationJournalData(UserJournal journalItem, UserJournal lastJournal, string integrationKey)
        {
            IntegrationJournalData ijd;
            if (journalItem.IntegrationData.ContainsKey(integrationKey))
            {
                ijd = journalItem.IntegrationData[integrationKey];
            }
            else
            {
                EDGameState oldState = null;

                if (lastJournal != null && lastJournal.IntegrationData != null && lastJournal.IntegrationData.ContainsKey(integrationKey) && lastJournal.IntegrationData[integrationKey].CurrentGameState != null)
                {
                    oldState = lastJournal.IntegrationData[integrationKey].CurrentGameState;
                }

                ijd = new IntegrationJournalData
                {
                    FullySent = false,
                    LastSentLineNumber = 0,
                    CurrentGameState = oldState ?? new EDGameState()
                };

                journalItem.IntegrationData.TryAdd(integrationKey, ijd);
            }

            ijd.CurrentGameState.SendEvents = true;
            return ijd;
        }

        public static async Task<JsonElement> SetGamestateProperties(JsonElement element, EDGameState gameState, string commander, StarSystemChecker starSystemChecker, Func<EDGameState, object> setProperties, Action<JsonElement, Dictionary<string, JsonElement>> addStateToElement = null)
        {
            var _rdb = SharedSettings.RedisClient.GetDatabase(1);
            var elementAsDictionary = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element.GetRawText());

            bool setCache = await EDSystemCache.GetSystemCache(elementAsDictionary, _rdb, starSystemChecker);

            GameStateChanger.GameStateFixer(gameState, commander, elementAsDictionary);

            var addItems = setProperties(gameState);

            var transientState = JsonDocument.Parse(JsonSerializer.Serialize(addItems)).RootElement;

            await EDSystemCache.SetSystemCache(gameState, _rdb, setCache);

            if (addStateToElement != null)
            {
                addStateToElement(transientState, elementAsDictionary);

                var json = JsonSerializer.Serialize(elementAsDictionary);
                return JsonDocument.Parse(json).RootElement;
            }

            return transientState;
        }
    }
}

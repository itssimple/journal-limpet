using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared.Database;
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
        public static async Task<EDGameState> LoadGameState(MSSQLDB db, Guid userIdentifier, List<UserJournal> userJournals, string integrationKey, PerformContext context)
        {
            EDGameState previousGameState = null;

            var firstAvailableGameState = userJournals.FirstOrDefault();
            if (firstAvailableGameState != null)
            {
                var previousJournal = await db.ExecuteSingleRowAsync<UserJournal>(
                    "SELECT TOP 1 * FROM user_journal WHERE user_identifier = @user_identifier AND journal_id <= @journal_id AND last_processed_line_number > 0 AND integration_data IS NOT NULL ORDER BY journal_date DESC",
                    new SqlParameter("user_identifier", userIdentifier),
                    new SqlParameter("journal_id", firstAvailableGameState.JournalId)
                );

                if (previousJournal != null && previousJournal.IntegrationData.ContainsKey(integrationKey))
                {
                    previousGameState = previousJournal.IntegrationData[integrationKey].CurrentGameState;

                    context.WriteLine($"Found previous gamestate: {JsonSerializer.Serialize(previousGameState, new JsonSerializerOptions { WriteIndented = true })}");
                }
            }

            return previousGameState;
        }
    }
}

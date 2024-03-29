﻿using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs
{
    public class JournalDownloadManager
    {
        public static async Task InitializeJournalDownloadersAsync(PerformContext context)
        {
            context.WriteLine("Looking for journals do download!");

            using (var scope = Startup.ServiceProvider.CreateScope())
            {
                MSSQLDB db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();
                IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                var usersToDownloadJournalsFor = await db.ExecuteListAsync<Shared.Models.User.Profile>(
    @"SELECT *
FROM user_profile
WHERE DATEDIFF(MINUTE, GETUTCDATE(), CAST(JSON_VALUE(user_settings, '$.TokenExpiration') as DATETIMEOFFSET)) > 10
AND last_notification_mail IS NULL
AND skip_download = 0
AND deleted = 0"
                );

                if (usersToDownloadJournalsFor.Count > 0)
                {
                    await SSEActivitySender.SendGlobalActivityAsync("Fetching journals from Elite", $"Downloading journals for {usersToDownloadJournalsFor.Count} users");
                }

                foreach (var user in usersToDownloadJournalsFor)
                {
                    if (RedisJobLock.IsLocked($"JournalDownloader.DownloadJournal.{user.UserIdentifier}")) continue;
                    BackgroundJob.Schedule(() => JournalDownloader.DownloadJournalAsync(user.UserIdentifier, null), TimeSpan.Zero);
                }
            }

            context.WriteLine("All done!");
        }
    }
}

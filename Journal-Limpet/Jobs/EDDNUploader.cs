﻿using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs
{
    public static class EDDNUploader
    {
        public static async Task UploadAsync(PerformContext context)
        {
            using (var rlock = new RedisJobLock($"EDDNUploader.UploadAsync"))
            {
                if (!rlock.TryTakeLock()) return;

                using (var scope = Startup.ServiceProvider.CreateScope())
                {
                    MSSQLDB db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();

                    var userToUploadToEDDN = await db.ExecuteListAsync<UnsentJournalInfo>(
        @"WITH UnsentJournals AS (
	SELECT uj.user_identifier, COUNT(uj.journal_id) journal_count
	FROM user_journal uj
	WHERE uj.last_processed_line_number > uj.sent_to_eddn_line AND uj.sent_to_eddn = 0
	GROUP BY uj.user_identifier
)
select uj.*
from user_profile up
INNER JOIN UnsentJournals uj ON up.user_identifier = uj.user_identifier
WHERE up.deleted = 0"
                    );

                    if (userToUploadToEDDN.Count > 0)
                    {
                        await SSEActivitySender.SendGlobalActivityAsync("Sending data to EDDN", $"Uploading journals from {userToUploadToEDDN.Count:N0} users to EDDN");
                    }

                    foreach (var user in userToUploadToEDDN.WithProgress(context))
                    {
                        if (RedisJobLock.IsLocked($"EDDNUserUploader.UploadAsync.{user.UserIdentifier}")) continue;
                        BackgroundJob.Schedule(() => EDDNUserUploader.UploadAsync(user.UserIdentifier, null), TimeSpan.Zero);
                    }
                }
            }
        }
    }
}

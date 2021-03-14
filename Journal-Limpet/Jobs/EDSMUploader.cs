using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs
{
    public static class EDSMUploader
    {
        public static async Task UploadAsync(PerformContext context)
        {
            using (var rlock = new RedisJobLock($"EDSMUploader.UploadAsync"))
            {
                if (!rlock.TryTakeLock()) return;

                using (var scope = Startup.ServiceProvider.CreateScope())
                {
                    MSSQLDB db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();

                    var userToUploadToEDSM = await db.ExecuteListAsync<UnsentJournalInfo>(
        @"WITH UnsentJournals AS (
	SELECT uj.user_identifier, COUNT_BIG(uj.journal_id) journal_count
	FROM user_journal uj
	WHERE uj.last_processed_line_number > ISNULL(JSON_VALUE(uj.integration_data, '$.EDSM.lastSentLineNumber'), 0)
    AND ISNULL(JSON_VALUE(uj.integration_data, '$.EDSM.fullySent'), 'false') = 'false'
	GROUP BY uj.user_identifier
)
select uj.*
from user_profile up
INNER JOIN UnsentJournals uj ON up.user_identifier = uj.user_identifier
WHERE up.deleted = 0
AND ISNULL(JSON_VALUE(up.integration_settings, '$.EDSM.enabled'), 'false') = 'true'
AND JSON_VALUE(up.integration_settings, '$.EDSM.apiKey') IS NOT NULL
AND JSON_VALUE(up.integration_settings, '$.EDSM.cmdrName') IS NOT NULL"
                    );

                    if (userToUploadToEDSM.Count > 0)
                    {
                        await SSEActivitySender.SendGlobalActivityAsync("Sending data to EDSM", $"Uploading journals from {userToUploadToEDSM.Count:N0} users to EDSM");
                    }

                    foreach (var user in userToUploadToEDSM.WithProgress(context))
                    {
                        if (RedisJobLock.IsLocked($"EDSMUserUploader.UploadAsync.{user.UserIdentifier}")) continue;
                        BackgroundJob.Schedule(() => EDSMUserUploader.UploadAsync(user.UserIdentifier, null), TimeSpan.Zero);
                    }
                }
            }
        }
    }
}

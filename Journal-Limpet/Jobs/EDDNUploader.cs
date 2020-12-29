using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared.Database;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Data;
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
	WHERE uj.last_processed_line_number > 0 AND uj.sent_to_eddn = 0
	GROUP BY uj.user_identifier
)
select uj.*
from user_profile up
INNER JOIN UnsentJournals uj ON up.user_identifier = uj.user_identifier
WHERE up.deleted = 0"
                    );

                    foreach (var user in userToUploadToEDDN.WithProgress(context))
                    {
                        if (RedisJobLock.IsLocked($"EDDNUserUploader.UploadAsync.{user.UserIdentifier}")) continue;
                        BackgroundJob.Schedule(() => EDDNUserUploader.UploadAsync(user.UserIdentifier, null), TimeSpan.Zero);
                    }
                }
            }
        }

        public class UnsentJournalInfo
        {
            public Guid UserIdentifier { get; set; }
            public int JournalCount { get; set; }

            public UnsentJournalInfo(DataRow row)
            {
                UserIdentifier = row.Field<Guid>("user_identifier");
                JournalCount = row.Field<int>("journal_count");
            }
        }
    }
}

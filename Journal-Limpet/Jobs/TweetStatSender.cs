using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs
{
    public class TweetStatSender
    {
        public static async Task SendStatsTweetAsync(PerformContext context)
        {
            context.WriteLine("Looking for tokens to refresh!");
            using (var scope = Startup.ServiceProvider.CreateScope())
            {
                IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                MSSQLDB db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();

                var tweetSender = scope.ServiceProvider.GetRequiredService<TwitterSender>();

                var mod = await db.ExecuteSingleRowAsync<IndexStatsModel>(@"SELECT CAST(COUNT(DISTINCT up.user_identifier) AS bigint) user_count, 
CAST(COUNT(journal_id) AS bigint) journal_count,
CAST(SUM(last_processed_line_number) AS bigint) total_number_of_lines
FROM user_profile up
LEFT JOIN user_journal uj ON up.user_identifier = uj.user_identifier AND uj.last_processed_line_number > 0
WHERE up.deleted = 0");

                var res = await tweetSender.SendAsync(
$@"Nightly stats #EliteDangerous

{mod.TotalUserCount.ToString("n0")} users registered
{mod.TotalUserJournalCount.ToString("n0")} journals saved
{mod.TotalUserJournalLines.ToString("n0")} lines of journal
https://journal-limpet.com");

                if (!res.status)
                {
                    await MailSender.SendSingleEmail(configuration, "no-reply+tweet@journal-limpet.com", "Failed to send tweet", res.response);
                }
            }
        }
    }
}

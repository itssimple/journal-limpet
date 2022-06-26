using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Text;
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

                var _rdb = SharedSettings.RedisClient.GetDatabase(0);

                var prevIndexStats = await _rdb.StringGetAsync("weekly-stats-json");

                var mod = await SharedSettings.GetIndexStatsAsync(db);

                StringBuilder sb = new StringBuilder();

                sb.AppendLine("Weekly stats #EliteDangerous");
                sb.AppendLine("");

                if (!prevIndexStats.IsNullOrEmpty)
                {
                    try
                    {
                        var prevIndexModel = JsonConvert.DeserializeObject<IndexStatsModel>(prevIndexStats);
                        var userDiff = mod.TotalUserCount - prevIndexModel.TotalUserCount;
                        var journalDiff = mod.TotalUserJournalCount - prevIndexModel.TotalUserJournalCount;
                        var linesDiff = mod.TotalUserJournalLines - prevIndexModel.TotalUserJournalLines;

                        if (linesDiff == 0 && journalDiff == 0 && userDiff == 0)
                        {
                            // No changes, no need to send tweet
                            return;
                        }

                        if (userDiff > 0)
                        {
                            sb.AppendLine($"{SharedSettings.NumberFixer(userDiff)} user(s) registered (Total {SharedSettings.NumberFixer(mod.TotalUserCount)})");
                        }

                        if (journalDiff > 0)
                        {
                            sb.AppendLine($"{SharedSettings.NumberFixer(journalDiff)} journal(s) saved (Total {SharedSettings.NumberFixer(mod.TotalUserJournalCount)})");
                        }

                        if (linesDiff > 0)
                        {
                            sb.AppendLine($"{SharedSettings.NumberFixer(linesDiff)} lines(s) saved (Total {SharedSettings.NumberFixer(mod.TotalUserJournalLines)})");
                        }
                    }
                    catch
                    {
                        sb.AppendLine($"{SharedSettings.NumberFixer(mod.TotalUserCount)} users registered");
                        sb.AppendLine($"{SharedSettings.NumberFixer(mod.TotalUserJournalCount)} journals saved");
                        sb.AppendLine($"{SharedSettings.NumberFixer(mod.TotalUserJournalLines)} lines of journal");
                    }
                }
                else
                {
                    sb.AppendLine($"{SharedSettings.NumberFixer(mod.TotalUserCount)} users registered");
                    sb.AppendLine($"{SharedSettings.NumberFixer(mod.TotalUserJournalCount)} journals saved");
                    sb.AppendLine($"{SharedSettings.NumberFixer(mod.TotalUserJournalLines)} lines of journal");
                }

                _rdb.StringSet("weekly-stats-json", JsonConvert.SerializeObject(mod));

                sb.AppendLine("https://journal-limpet.com");

                var res = await tweetSender.SendAsync(sb.ToString());

                if (!res.status)
                {
                    await MailSender.SendSingleEmail(configuration, "no-reply+tweet@journal-limpet.com", "Failed to send tweet", res.response);
                }
            }
        }
    }
}

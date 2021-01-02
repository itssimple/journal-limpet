﻿using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
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

                var mod = await SharedSettings.GetIndexStatsAsync(db);

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

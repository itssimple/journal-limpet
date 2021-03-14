using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Journal_Limpet.Shared;
using Journal_Limpet.Shared.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet.Jobs
{
    public class EliteSystemsUpdater
    {
        [JobDisplayName("Elite System Updater")]
        public static async Task UpdateEliteSystemsAsync(PerformContext context)
        {
            using (var rlock = new RedisJobLock($"EliteSystemsUpdater.UpdateEliteSystemsAsync"))
            {
                if (!rlock.TryTakeLock()) return;
                context.WriteLine("Checking if we need to update the starsystem");

                var _rdb = SharedSettings.RedisClient.GetDatabase(0);
                var lastCachedUpdate = await _rdb.StringGetAsync("spansh:latestUpdate");

                long systems = 0;

                var url = "https://downloads.spansh.co.uk/galaxy_1day.json.gz";

                if (lastCachedUpdate.IsNull)
                {
                    url = "https://downloads.spansh.co.uk/galaxy_7days.json.gz";
                }

                var batchOfBatches = new List<List<string>>();
                var batchInserts = new List<string>();

                using (var scope = Startup.ServiceProvider.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();
                    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    var hc = SharedSettings.GetHttpClient(scope);

                    var lastUpdateCheck = await hc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                    context.WriteLine($"Spansh dump was updated: {lastUpdateCheck.Content.Headers.LastModified}");

                    if (lastCachedUpdate.IsNull || lastCachedUpdate != lastUpdateCheck.Content.Headers.LastModified.ToString())
                    {
                        context.WriteLine($"Downloading new system dump from {url}");
                        using (var beep = await hc.GetStreamAsync(url))
                        using (var fileStream = new BufferedStream(beep))
                        using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                        using (var textReader = new StreamReader(gzipStream))
                        {
                            while (!textReader.EndOfStream)
                            {
                                var line = textReader.ReadLine();
                                if (!string.IsNullOrWhiteSpace(line) && line.Trim() != "[" && line.Trim() != "]")
                                {
                                    line = line.Trim(',').Trim();
                                    if (line.StartsWith("{") && line.EndsWith("}"))
                                    {
                                        systems++;
                                        var sys = JsonSerializer.Deserialize<EDSystemData>(line);
                                        var jsPos = JsonSerializer.Serialize(sys.Coordinates);

                                        var sql = $@"({sys.Id64}, '{sys.Name.Replace("'", "''")}', '{jsPos}')";
                                        batchInserts.Add(sql);

                                        if (batchInserts.Count == 1000)
                                        {
                                            batchOfBatches.Add(batchInserts);
                                            batchInserts.Clear();
                                        }
                                    }
                                }
                            }
                        }

                        if (batchInserts.Count > 0)
                        {
                            batchOfBatches.Add(batchInserts);
                            batchInserts.Clear();
                        }

                        context.WriteLine($"Fetched {systems} from {url}, generated {batchOfBatches.Count} batches of inserts. Running them now.");

                        foreach (var batch in batchOfBatches.WithProgress(context))
                        {
                            await ExecuteBatchInsert(batch, db);
                        }
                    }

                    await _rdb.StringSetAsyncWithRetries("spansh:latestUpdate", lastUpdateCheck.Content.Headers.LastModified.ToString(), TimeSpan.FromHours(23));
                }

                context.WriteLine("All done!");
            }
        }

        private static async Task ExecuteBatchInsert(List<string> batchInserts, MSSQLDB db)
        {
            var batch = $@"BEGIN TRANSACTION;
INSERT INTO EliteSystem (SystemAddress, StarSystem, StarPos)
VALUES
{string.Join(",\n", batchInserts)}
COMMIT TRANSACTION";

            await db.ExecuteNonQueryAsync(batch);
            batchInserts.Clear();
        }
    }
}

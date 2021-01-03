using Journal_Limpet.Shared.Database;
using Polly;
using Polly.Retry;
using StackExchange.Redis;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet
{
    public static class SSEActivitySender
    {
        public static async Task SendGlobalActivityAsync(string title, string message, string @class = "warning")
        {
            var msg = new
            {
                title,
                message,
                @class
            };

            await GetRedisRetryPolicy().ExecuteAsync(() => SharedSettings.RedisPubsubSubscriber.PublishAsync("global-activity", JsonSerializer.Serialize(msg), CommandFlags.FireAndForget));
        }

        public static async Task SendUserActivityAsync(Guid userIdentifier, string title, string message, string @class = "warning")
        {
            var msg = new
            {
                title,
                message,
                @class
            };

            await GetRedisRetryPolicy().ExecuteAsync(() => SharedSettings.RedisPubsubSubscriber.PublishAsync($"user-activity-{userIdentifier}", JsonSerializer.Serialize(msg), CommandFlags.FireAndForget));
        }

        public static async Task SendStatsActivityAsync(MSSQLDB _db)
        {
            try
            {
                var stats = await SharedSettings.GetIndexStatsAsync(_db);
                await SharedSettings.RedisPubsubSubscriber.PublishAsync($"stats-activity", JsonSerializer.Serialize(stats), CommandFlags.FireAndForget);
            }
            catch
            {
                // It is better is we don't send stats, instead of possibly sending the wrong one
            }
        }

        private static AsyncRetryPolicy<long> GetRedisRetryPolicy()
        {
            return Policy<long>
                .Handle<RedisTimeoutException>()
                .WaitAndRetryAsync(10, attempt => TimeSpan.FromSeconds(1));
        }
    }
}

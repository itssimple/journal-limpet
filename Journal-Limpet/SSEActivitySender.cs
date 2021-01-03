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

            await GetRedisRetryPolicy().ExecuteAsync(() => SharedSettings.RedisPubsubSubscriber.PublishAsync("global-activity", JsonSerializer.Serialize(msg)));
        }

        public static async Task SendUserActivityAsync(Guid userIdentifier, string title, string message, string @class = "warning")
        {
            var msg = new
            {
                title,
                message,
                @class
            };

            await GetRedisRetryPolicy().ExecuteAsync(() => SharedSettings.RedisPubsubSubscriber.PublishAsync($"user-activity-{userIdentifier}", JsonSerializer.Serialize(msg)));
        }

        public static async Task SendStatsActivityAsync(MSSQLDB _db)
        {
            var stats = await SharedSettings.GetIndexStatsAsync(_db);
            await GetRedisRetryPolicy().ExecuteAsync(() => SharedSettings.RedisPubsubSubscriber.PublishAsync($"stats-activity", JsonSerializer.Serialize(stats)));
        }

        private static AsyncRetryPolicy<long> GetRedisRetryPolicy()
        {
            return Policy<long>
                .Handle<RedisTimeoutException>()
                .WaitAndRetryAsync(10, attempt => TimeSpan.FromSeconds(1));
        }
    }
}

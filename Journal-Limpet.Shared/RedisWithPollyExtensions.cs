using Polly;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace Journal_Limpet.Shared
{
    public static class RedisWithPollyExtensions
    {
        public static async Task<RedisValue> StringGetAsyncWithRetries(this IDatabase _rdb, RedisKey key, int retries = 10)
        {
            try
            {
                var retryPolicy = Policy<RedisValue>
                    .Handle<RedisTimeoutException>()
                    .WaitAndRetryAsync(retries, attempt => TimeSpan.FromSeconds(1 * attempt));

                return await retryPolicy.ExecuteAsync(() => _rdb.StringGetAsync(key));
            }
            catch
            {
                return RedisValue.Null;
            }
        }

        public static async Task<bool> StringSetAsyncWithRetries(this IDatabase _rdb, RedisKey key, RedisValue value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None, int retries = 10)
        {
            try
            {
                var retryPolicy = Policy<bool>
                    .Handle<RedisTimeoutException>()
                    .WaitAndRetryAsync(retries, attempt => TimeSpan.FromSeconds(1 * attempt));

                return await retryPolicy.ExecuteAsync(() => _rdb.StringSetAsync(key, value, expiry, flags: flags));
            }
            catch
            {
                return false;
            }
        }
    }
}

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

        public static async Task<RedisValue> StringGetAsyncWithRetriesSaveIfMissing(this IDatabase _rdb, RedisKey key, int retries = 10, Func<Task<string>> ifMissing = null)
        {
            var redisValue = await StringGetAsyncWithRetries(_rdb, key, retries);
            if (redisValue.IsNull && ifMissing != null)
            {
                var newValue = await ifMissing();
                if (!string.IsNullOrWhiteSpace(newValue))
                {
                    await StringSetAsyncWithRetries(_rdb, key, newValue, TimeSpan.FromHours(24), CommandFlags.FireAndForget);
                    return newValue;
                }
            }

            return redisValue;
        }
    }
}

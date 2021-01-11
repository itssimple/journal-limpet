using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using StackExchange.Redis;
using System.Threading.Tasks;

namespace Journal_Limpet
{
    public static class SharedSettings
    {
        private static ConnectionMultiplexer _redisClient;

        public static string VersionNumber = "{{version}}";

        public static ConnectionMultiplexer RedisClient => _redisClient ?? (_redisClient = ConnectionMultiplexer.Connect("127.0.0.1:6379"));

        private static ISubscriber _redisPubsubSubscriber;
        public static ISubscriber RedisPubsubSubscriber = _redisPubsubSubscriber ?? (_redisPubsubSubscriber = RedisClient.GetSubscriber());

        public static async Task<IndexStatsModel> GetIndexStatsAsync(MSSQLDB _db)
        {
            return await _db.ExecuteSingleRowAsync<IndexStatsModel>(@"SELECT CAST(COUNT(DISTINCT up.user_identifier) AS bigint) user_count,
CAST(COUNT(journal_id) AS bigint) journal_count,
CAST(SUM(last_processed_line_number) AS bigint) total_number_of_lines
FROM user_profile up
LEFT JOIN user_journal uj ON up.user_identifier = uj.user_identifier AND uj.last_processed_line_number > 0
WHERE up.deleted = 0");
        }
    }
}

using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
            return await _db.ExecuteSingleRowAsync<IndexStatsModel>(@"SELECT COUNT_BIG(DISTINCT up.user_identifier) user_count,
COUNT_BIG(journal_id) journal_count,
SUM(last_processed_line_number) total_number_of_lines
FROM user_profile up
LEFT JOIN user_journal uj ON up.user_identifier = uj.user_identifier AND uj.last_processed_line_number > 0
WHERE up.deleted = 0");
        }

        public static HttpClient GetHttpClient(IServiceScope scope)
        {
            var _hcf = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            var hc = _hcf.CreateClient();
            hc.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Journal-Limpet", SharedSettings.VersionNumber));
            return hc;
        }

        public static string NumberFixer(long number)
        {
            if (number == 0)
            {
                return "Zero";
            }
            else if (number < 499)
            {
                return number.ToString("n0");
            }
            else if (number < 4_999)
            {
                return $"{EmptyDecimalRemover(number.ToString("n1"))}";
            }
            else if (number < 499_999)
            {
                return $"{EmptyDecimalRemover((number / (double)1000).ToString("n1"))} thousand";
            }
            else if (number < 499_999_999)
            {
                return $"{EmptyDecimalRemover((number / (double)1000 / 1000).ToString("n1"))} million";
            }

            return $"{EmptyDecimalRemover((number / (double)1000 / 1000 / 1000).ToString("n1"))} billion";
        }

        private static string EmptyDecimalRemover(string formattedNumber)
        {
            return formattedNumber.Replace($"{CultureInfo.CurrentCulture.NumberFormat.CurrencyDecimalSeparator[0]}0", string.Empty);
        }

        public static string GetHashedValue(string value, string salt)
        {
            return BitConverter.ToString(SHA512.HashData(Encoding.UTF8.GetBytes(value + salt))).Replace("-", string.Empty);
        }
    }
}

using Hangfire;
using Hangfire.Logging;
using Journal_Limpet.Shared.Database;
using Journal_Limpet.Shared.Models;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Journal_Limpet
{
    public static class SharedSettings
    {
        public static TimeZoneInfo SwedishTimeZone => TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        private static int _jobCount = 100;

        private static ConnectionMultiplexer _redisClient;

        public static string VersionNumber = "{{version}}";

        public static ConnectionMultiplexer RedisClient => _redisClient ?? (_redisClient = ConnectionMultiplexer.Connect("127.0.0.1:6379"));

        private static ISubscriber _redisPubsubSubscriber;
        public static ISubscriber RedisPubsubSubscriber = _redisPubsubSubscriber ?? (_redisPubsubSubscriber = RedisClient.GetSubscriber());

        public static bool CheckIfTaskIsScheduledOrInProgress(string taskName, string methodName)
        {
            if (RedisJobLock.IsLocked($"{taskName}.{methodName}"))
            {
                return true;
            }

            var mapi = JobStorage.Current.GetMonitoringApi();

            var processing = mapi.ProcessingJobs(0, _jobCount).Where(i => i.Value.Job != null).Select(i => string.Format("{0}.{1}", i.Value.Job.Method.DeclaringType.Name, i.Value.Job.Method.Name));
            var scheduled = mapi.ScheduledJobs(0, _jobCount).Where(i => i.Value.Job != null).Select(i => string.Format("{0}.{1}", i.Value.Job.Method.DeclaringType.Name, i.Value.Job.Method.Name));
            var enqueued = mapi.EnqueuedJobs("default", 0, _jobCount).Where(i => i.Value.Job != null).Select(i => string.Format("{0}.{1}", i.Value.Job.Method.DeclaringType.Name, i.Value.Job.Method.Name));

            var jobs = processing.Concat(scheduled).Concat(enqueued);

            return jobs.Any(s => s == $"{taskName}.{methodName}");
        }

        public static async Task<IndexStatsModel> GetIndexStatsAsync(MSSQLDB _db)
        {
            return await _db.ExecuteSingleRowAsync<IndexStatsModel>(@"SELECT CAST(COUNT(DISTINCT up.user_identifier) AS bigint) user_count, 
CAST(COUNT(journal_id) AS bigint) journal_count,
CAST(SUM(last_processed_line_number) AS bigint) total_number_of_lines
FROM user_profile up
LEFT JOIN user_journal uj ON up.user_identifier = uj.user_identifier AND uj.last_processed_line_number > 0
WHERE up.deleted = 0");
        }

        public static void AddImportantJob(string className, string methodName, Expression<Action> action, TimeSpan startInterval, TimeSpan? subsequentInterval = null)
        {
            if (ImportantJobs.TryAdd(className + ":" + methodName, (className, methodName, action, startInterval, subsequentInterval ?? startInterval)) &&
                !CheckIfTaskIsScheduledOrInProgress(className, methodName))
            {
                BackgroundJob.Schedule(action, startInterval);
            }
        }

        public static void AddImportantJob(string className, string methodName, Expression<Func<Task>> action, TimeSpan startInterval, TimeSpan? subsequentInterval = null)
        {
            if (ImportantJobs.TryAdd(className + ":" + methodName, (className, methodName, action, startInterval, subsequentInterval ?? startInterval)) &&
                !CheckIfTaskIsScheduledOrInProgress(className, methodName))
            {
                BackgroundJob.Schedule(action, startInterval);
            }
        }

        public static ConcurrentDictionary<string, (string className, string methodName, object action, TimeSpan startInterval, TimeSpan subsequentInterval)> ImportantJobs =
            new ConcurrentDictionary<string, (string className, string methodName, object action, TimeSpan startInterval, TimeSpan subsequentInterval)>();


        public static ILog GetExceptionalLogger()
        {
            return LogProvider.GetCurrentClassLogger();
        }

        public static string GetResourceFullName(string resourceName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            string[] resourceCollection = assembly.GetManifestResourceNames();

            return resourceCollection.FirstOrDefault(i => i.Contains(resourceName));
        }
    }
}

using StackExchange.Redis;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Journal_Limpet
{
    public class RedisJobLock : IDisposable
    {
        private string TypeName { get; set; }
        private string Handle { get; set; }

        private IDatabase RDB { get { return getRedisDatabase(); } }
        private Guid LockInstance;
        private CancellationToken _cancellationToken;
        private readonly CancellationTokenSource CTS;

        public RedisJobLock(string lockName)
        {
            CTS = new CancellationTokenSource();

            TypeName = lockName;
            LockInstance = Guid.NewGuid();
            _cancellationToken = CTS.Token;

            Handle = GetHandle(TypeName);
        }

        public static bool IsLocked(string lockName)
        {
            var rdb = getRedisDatabase();
            var handle = GetHandle(lockName);
            return rdb.LockQuery(handle) != RedisValue.Null;
        }

        private static string GetHandle(string lockName)
        {
            return $"joblock:{lockName}";
        }

        private static IDatabase getRedisDatabase()
        {
            return SharedSettings.RedisClient.GetDatabase(0);
        }

        TimeSpan LockTime = TimeSpan.FromSeconds(15);

        Task _refreshLockTask;
        async Task RefreshLockAsync()
        {
            try
            {
                await Task.Delay(LockTime.Subtract(TimeSpan.FromMilliseconds(LockTime.TotalMilliseconds / 2)), _cancellationToken);
                KeepLocked();
                _refreshLockTask = RefreshLockAsync();
                _ = PubSubLog($"Refreshing lock {Handle} / {LockInstance}");
            }
            catch (TaskCanceledException)
            {
                /* This is totally ok, we cancelled it */
            }
        }

        private void KeepLocked()
        {
            RDB.LockExtend(Handle, LockInstance.ToString(), LockTime);
        }

        public async Task KeepLockedAsync()
        {
            await RDB.LockExtendAsync(Handle, LockInstance.ToString(), LockTime);
        }

        public bool TryTakeLock()
        {
            var couldTakeLock = RDB.LockTake(Handle, LockInstance.ToString(), LockTime);
            if (couldTakeLock) _refreshLockTask = RefreshLockAsync();
            _ = PubSubLog($"Could {(couldTakeLock ? "" : "not ")}take lock for {Handle} / {LockInstance}");
            return couldTakeLock;
        }

        public async Task<bool> TryTakeLockAsync()
        {
            var couldTakeLock = await RDB.LockTakeAsync(Handle, LockInstance.ToString(), LockTime);
            if (couldTakeLock) _refreshLockTask = RefreshLockAsync();
            _ = PubSubLog($"Could {(couldTakeLock ? "" : "not ")}take lock for {Handle} / {LockInstance}");
            return couldTakeLock;
        }

        internal async Task PubSubLog(string logmessage)
        {
            await SharedSettings.RedisClient.GetSubscriber().PublishAsync("LockMessages/JournalLimpet", logmessage);
        }

        public void Dispose()
        {
            _ = PubSubLog($"Releasing lock {Handle} / {LockInstance}");
            RDB.LockRelease(Handle, LockInstance.ToString());
            CTS.Cancel();
        }
    }
}

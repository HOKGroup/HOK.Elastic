using Microsoft.Extensions.Caching.Memory;
using System;

namespace HOK.Elastic.Logger
{
    public class ExceptionRateLimiter
    {
        private static MemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions() { CompactionPercentage = 0.2 });
        /// <summary>
        /// Fires when the number of exceptions > ThresholdReached occuring within the ThresholdTime timespan.
        /// </summary>
        public static event EventHandler ThresholdReached;
        /// <summary>
        /// Number of consecutive exceptions that need to occur with Thresholdtime before firing
        /// </summary>
        public static int ThresholdCount { get; set; } = 50;

        /// <summary>
        /// After ThresholdTime elapses, the Exception ThresholdCount is reset to zero
        /// </summary>
        public static TimeSpan ThresholdTime { get; set; } = TimeSpan.FromSeconds(20);
        private static int currentCount = 0;
        public static bool HasRateLimitExceeded(Exception ex)
        {
            if (ex == null) return false;
            string key = ex.Message;
            bool stop = false;
            object smallresult;

            _memoryCache.TryGetValue(key, out smallresult);
            if (smallresult != null)
            {
                currentCount = 0;//reset the counter back to zero.
                _memoryCache.CreateEntry(key);
                _memoryCache.Set(key, true, new MemoryCacheEntryOptions() { AbsoluteExpirationRelativeToNow = ThresholdTime, Priority = CacheItemPriority.Low });
            }
            else
            {
                currentCount++;
            }
            if (currentCount > ThresholdCount)
            {
                stop = true;
            }
            if (stop)
            {
                OnThresholdReached(EventArgs.Empty);
            }
            return stop;
        }

        protected static void OnThresholdReached(EventArgs e)
        {
            EventHandler handler = ThresholdReached;
            handler?.Invoke(null, e);
        }
    }
}

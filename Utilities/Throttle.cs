using System;
using System.Collections.Concurrent;

namespace FFXIManager.Utilities
{
    public static class Throttle
    {
        private static readonly ConcurrentDictionary<string, DateTime> _lastRunUtc = new();

        public static bool ShouldRun(string key, TimeSpan interval, DateTime? nowUtc = null)
        {
            var now = nowUtc ?? DateTime.UtcNow;
            var last = _lastRunUtc.GetOrAdd(key, _ => DateTime.MinValue);
            if (now - last > interval)
            {
                _lastRunUtc[key] = now;
                return true;
            }
            return false;
        }

        public static void Reset(string key)
        {
            _lastRunUtc.TryRemove(key, out _);
        }
    }
}

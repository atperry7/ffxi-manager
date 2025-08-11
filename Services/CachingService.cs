using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for caching operations
    /// </summary>
    public interface ICachingService
    {
        Task<T?> GetAsync<T>(string key) where T : class;
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class;
        Task RemoveAsync(string key);
        Task ClearAsync();
        Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null) where T : class;
        bool Contains(string key);
    }

    /// <summary>
    /// Cache entry with expiration support
    /// </summary>
    internal sealed class CacheEntry
    {
        public object Value { get; set; } = null!;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ExpiresAt { get; set; }
        public bool IsExpired => ExpiresAt.HasValue && DateTime.Now > ExpiresAt.Value;
    }

    /// <summary>
    /// In-memory caching service with TTL support
    /// </summary>
    public class CachingService : ICachingService, IDisposable
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private readonly TimeSpan _defaultExpiration;
        private readonly System.Threading.Timer _cleanupTimer;

        public CachingService(TimeSpan? defaultExpiration = null)
        {
            _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(30);
            
            // Set up cleanup timer to run every 5 minutes
            _cleanupTimer = new System.Threading.Timer(CleanupExpiredEntries, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            return await Task.Run(() =>
            {
                if (!_cache.TryGetValue(key, out var entry)) 
                    return null;

                if (entry.IsExpired)
                {
                    _cache.TryRemove(key, out _);
                    return null;
                }

                return entry.Value as T;
            });
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
        {
            await Task.Run(() =>
            {
                var expirationTime = expiration ?? _defaultExpiration;
                var entry = new CacheEntry
                {
                    Value = value,
                    CreatedAt = DateTime.Now,
                    ExpiresAt = expirationTime == TimeSpan.MaxValue ? null : DateTime.Now.Add(expirationTime)
                };

                _cache.AddOrUpdate(key, entry, (_, _) => entry);
            });
        }

        public async Task RemoveAsync(string key)
        {
            await Task.Run(() => _cache.TryRemove(key, out _));
        }

        public async Task ClearAsync()
        {
            await Task.Run(() => _cache.Clear());
        }

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null) where T : class
        {
            var cached = await GetAsync<T>(key);
            if (cached != null)
                return cached;

            var value = await factory();
            await SetAsync(key, value, expiration);
            return value;
        }

        public bool Contains(string key)
        {
            if (!_cache.TryGetValue(key, out var entry))
                return false;

            if (entry.IsExpired)
            {
                _cache.TryRemove(key, out _);
                return false;
            }

            return true;
        }

        private void CleanupExpiredEntries(object? state)
        {
            var expiredKeys = new List<string>();
            
            foreach (var kvp in _cache)
            {
                if (kvp.Value.IsExpired)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Static cache keys for commonly cached items
    /// </summary>
    public static class CacheKeys
    {
        public const string ProfilesList = "profiles_list";
        public const string UserProfilesList = "user_profiles_list";
        public const string AutoBackupsList = "auto_backups_list";
        public const string ActiveLoginInfo = "active_login_info";
        public const string DirectoryValidation = "directory_validation_{0}";
        public const string FileValidation = "file_validation_{0}";
    }
}
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Centralized context management service for AutoLogin tasks.
    /// Provides thread-safe context storage with automatic lifecycle management.
    /// </summary>
    public class AutoLoginContextService : IAutoLoginContextService, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly ConcurrentDictionary<string, IAutoLoginContext> _contexts = new();
        private readonly Timer _cleanupTimer;
        private readonly TimeSpan _expirationTime;
        private readonly TimeSpan _cleanupInterval;
        private readonly int _maxContextsPerUser;
        private bool _disposed;

        public AutoLoginContextService(ILoggingService loggingService, IConfiguration? configuration = null)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            // Load configuration with sensible defaults
            var contextConfig = configuration?.GetSection("AutoLogin:Context");
            _expirationTime = TimeSpan.FromMinutes(contextConfig?.GetValue<int>("ExpirationMinutes") ?? 30);
            var cleanupMinutes = contextConfig?.GetValue<int>("CleanupIntervalMinutes") ?? 5;
            _cleanupInterval = TimeSpan.FromMinutes(cleanupMinutes);
            _maxContextsPerUser = contextConfig?.GetValue<int>("MaxContextsPerUser") ?? 10;

            // Start cleanup timer
            _cleanupTimer = new Timer(async _ => await CleanupExpiredContextsAsync(),
                null, _cleanupInterval, _cleanupInterval);

            _loggingService.LogDebugAsync($"AutoLoginContextService initialized with {_expirationTime.TotalMinutes}min expiration, {cleanupMinutes}min cleanup interval");
        }

        public int ActiveContextCount => _contexts.Count;

        public IAutoLoginContext GetContext(string queueItemId)
        {
            if (string.IsNullOrEmpty(queueItemId))
                throw new ArgumentException("Queue item ID cannot be null or empty", nameof(queueItemId));

            return _contexts.GetOrAdd(queueItemId, id =>
            {
                var context = new AutoLoginContext(id, _loggingService);
                _loggingService.LogDebugAsync($"Created new AutoLogin context for queue item: {id}");
                return context;
            });
        }

        public async Task<bool> ValidateContextAsync(string queueItemId, string[] requiredKeys)
        {
            if (string.IsNullOrEmpty(queueItemId))
                throw new ArgumentException("Queue item ID cannot be null or empty", nameof(queueItemId));

            if (requiredKeys == null || requiredKeys.Length == 0)
                return true; // No requirements, validation passes

            if (!_contexts.TryGetValue(queueItemId, out var context))
            {
                await _loggingService.LogWarningAsync($"Context validation failed: No context found for queue item {queueItemId}");
                return false;
            }

            var missingKeys = requiredKeys.Where(key => !context.HasData(key)).ToArray();
            if (missingKeys.Any())
            {
                await _loggingService.LogWarningAsync($"Context validation failed for {queueItemId}: Missing keys: {string.Join(", ", missingKeys)}");
                return false;
            }

            await _loggingService.LogDebugAsync($"Context validation passed for {queueItemId}: All required keys present");
            return true;
        }

        public async Task DisposeContextAsync(string queueItemId)
        {
            if (string.IsNullOrEmpty(queueItemId))
                throw new ArgumentException("Queue item ID cannot be null or empty", nameof(queueItemId));

            if (_contexts.TryRemove(queueItemId, out var context))
            {
                context.Dispose();
                await _loggingService.LogDebugAsync($"Disposed context for queue item: {queueItemId}");
            }
        }

        public async Task CleanupExpiredContextsAsync()
        {
            if (_disposed) return;

            try
            {
                var cutoffTime = DateTime.UtcNow - _expirationTime;
                var expiredContexts = _contexts
                    .Where(kvp => kvp.Value.LastAccessed < cutoffTime)
                    .Select(kvp => kvp.Key)
                    .ToArray();

                foreach (var queueItemId in expiredContexts)
                {
                    await DisposeContextAsync(queueItemId);
                }

                if (expiredContexts.Length > 0)
                {
                    await _loggingService.LogInfoAsync($"Cleaned up {expiredContexts.Length} expired AutoLogin contexts");
                }

                // Also enforce max contexts per user (simple implementation for now)
                if (_contexts.Count > _maxContextsPerUser)
                {
                    var oldestContexts = _contexts
                        .OrderBy(kvp => kvp.Value.LastAccessed)
                        .Take(_contexts.Count - _maxContextsPerUser)
                        .Select(kvp => kvp.Key)
                        .ToArray();

                    foreach (var queueItemId in oldestContexts)
                    {
                        await DisposeContextAsync(queueItemId);
                    }

                    if (oldestContexts.Length > 0)
                    {
                        await _loggingService.LogInfoAsync($"Cleaned up {oldestContexts.Length} oldest contexts to enforce max limit");
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error during context cleanup", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _cleanupTimer?.Dispose();

            // Dispose all active contexts
            foreach (var context in _contexts.Values)
            {
                context.Dispose();
            }
            _contexts.Clear();

            _disposed = true;
        }
    }

    /// <summary>
    /// Implementation of AutoLogin context with thread-safe data storage and lifecycle tracking.
    /// </summary>
    internal class AutoLoginContext : IAutoLoginContext
    {
        private readonly ILoggingService _loggingService;
        private readonly object _accessLock = new();
        private DateTime _lastAccessed;
        private bool _disposed;

        public AutoLoginContext(string queueItemId, ILoggingService loggingService)
        {
            QueueItemId = queueItemId ?? throw new ArgumentNullException(nameof(queueItemId));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            CreatedAt = DateTime.UtcNow;
            _lastAccessed = CreatedAt;
            Data = new ConcurrentDictionary<string, object>();
        }

        public string QueueItemId { get; }
        public DateTime CreatedAt { get; }
        public ConcurrentDictionary<string, object> Data { get; }

        public DateTime LastAccessed
        {
            get
            {
                lock (_accessLock)
                {
                    return _lastAccessed;
                }
            }
        }

        public void SetData<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            if (value == null)
                throw new ArgumentNullException(nameof(value));

            UpdateLastAccessed();
            Data.AddOrUpdate(key, value, (k, oldValue) => value);
            _loggingService.LogDebugAsync($"Context [{QueueItemId}]: Set {key} = {typeof(T).Name}");
        }

        public T? GetData<T>(string key) where T : class
        {
            if (string.IsNullOrEmpty(key))
                return null;

            UpdateLastAccessed();
            if (Data.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }

            return null;
        }

        public T GetValueData<T>(string key) where T : struct
        {
            if (string.IsNullOrEmpty(key))
                return default(T);

            UpdateLastAccessed();
            if (Data.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }

            return default(T);
        }

        public bool HasData(params string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return true;

            UpdateLastAccessed();
            return keys.All(key => !string.IsNullOrEmpty(key) && Data.ContainsKey(key));
        }

        private void UpdateLastAccessed()
        {
            lock (_accessLock)
            {
                _lastAccessed = DateTime.UtcNow;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            Data.Clear();
            _disposed = true;
        }
    }
}
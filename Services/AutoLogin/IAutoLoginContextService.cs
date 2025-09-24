using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Centralized context management service for AutoLogin tasks.
    /// Provides thread-safe context storage with automatic lifecycle management.
    /// </summary>
    public interface IAutoLoginContextService
    {
        /// <summary>
        /// Gets or creates context for a queue item
        /// </summary>
        /// <param name="queueItemId">The unique identifier for the queue item</param>
        /// <returns>Context instance for the queue item</returns>
        IAutoLoginContext GetContext(string queueItemId);

        /// <summary>
        /// Validates that required context data is available
        /// </summary>
        /// <param name="queueItemId">The queue item identifier</param>
        /// <param name="requiredKeys">Array of keys that must exist in the context</param>
        /// <returns>True if all required keys exist, false otherwise</returns>
        Task<bool> ValidateContextAsync(string queueItemId, string[] requiredKeys);

        /// <summary>
        /// Cleans up context when queue item completes or fails
        /// </summary>
        /// <param name="queueItemId">The queue item identifier to clean up</param>
        Task DisposeContextAsync(string queueItemId);

        /// <summary>
        /// Cleans up all expired contexts based on configured expiration time
        /// </summary>
        Task CleanupExpiredContextsAsync();

        /// <summary>
        /// Gets the total number of active contexts (for monitoring/diagnostics)
        /// </summary>
        int ActiveContextCount { get; }
    }

    /// <summary>
    /// Represents context data for a specific AutoLogin queue item.
    /// Provides thread-safe data storage and lifecycle management.
    /// </summary>
    public interface IAutoLoginContext : IDisposable
    {
        /// <summary>
        /// The queue item identifier this context belongs to
        /// </summary>
        string QueueItemId { get; }

        /// <summary>
        /// When this context was created
        /// </summary>
        DateTime CreatedAt { get; }

        /// <summary>
        /// When this context was last accessed
        /// </summary>
        DateTime LastAccessed { get; }

        /// <summary>
        /// Thread-safe context data storage
        /// </summary>
        ConcurrentDictionary<string, object> Data { get; }

        /// <summary>
        /// Store typed data with validation
        /// </summary>
        /// <typeparam name="T">Type of data to store</typeparam>
        /// <param name="key">Unique key for the data</param>
        /// <param name="value">Value to store</param>
        void SetData<T>(string key, T value);

        /// <summary>
        /// Retrieve typed data with validation
        /// </summary>
        /// <typeparam name="T">Expected type of the data</typeparam>
        /// <param name="key">Key to retrieve</param>
        /// <returns>Typed value or null if not found or wrong type</returns>
        T? GetData<T>(string key) where T : class;

        /// <summary>
        /// Retrieve typed value data (for value types)
        /// </summary>
        /// <typeparam name="T">Expected value type</typeparam>
        /// <param name="key">Key to retrieve</param>
        /// <returns>Typed value or default if not found or wrong type</returns>
        T GetValueData<T>(string key) where T : struct;

        /// <summary>
        /// Check if required data exists
        /// </summary>
        /// <param name="keys">Keys to check for existence</param>
        /// <returns>True if all keys exist, false otherwise</returns>
        bool HasData(params string[] keys);
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Base interface for all auto-login task handlers.
    /// Each handler implements the automation logic for workflow-driven subtasks.
    /// </summary>
    public interface ILoginTaskHandler
    {
        /// <summary>
        /// Whether this handler can execute the specified subtask
        /// </summary>
        bool CanHandle(AutoLoginSubtask subtask);

        /// <summary>
        /// Executes the automation logic for the given subtask
        /// </summary>
        /// <param name="subtask">The subtask to execute</param>
        /// <param name="queueItem">The queue item containing account and context information</param>
        /// <param name="context">The context for sharing data between handlers</param>
        /// <param name="cancellationToken">Cancellation token for stopping execution</param>
        /// <returns>Task representing the async operation</returns>
        Task ExecuteAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Context information passed to task handlers during execution.
    /// Now implements IAutoLoginContext for centralized context management.
    /// </summary>
    public class TaskExecutionContext : IAutoLoginContext
    {
        private readonly object _accessLock = new();
        private DateTime _lastAccessed;
        private bool _disposed;

        public TaskExecutionContext(string queueItemId)
        {
            if (string.IsNullOrEmpty(queueItemId))
                throw new ArgumentException("Queue item ID cannot be null or empty", nameof(queueItemId));

            QueueItemId = queueItemId;
            CreatedAt = DateTime.UtcNow;
            _lastAccessed = CreatedAt;
            Data = new ConcurrentDictionary<string, object>();
            SharedData = new Dictionary<string, object>(); // Keep for backward compatibility
        }

        #region IAutoLoginContext Implementation

        /// <summary>
        /// The queue item identifier this context belongs to
        /// </summary>
        public string QueueItemId { get; }

        /// <summary>
        /// When this context was created
        /// </summary>
        public DateTime CreatedAt { get; }

        /// <summary>
        /// When this context was last accessed
        /// </summary>
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

        /// <summary>
        /// Thread-safe context data storage
        /// </summary>
        public ConcurrentDictionary<string, object> Data { get; }

        /// <summary>
        /// Store typed data with validation
        /// </summary>
        public void SetData<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            if (value == null)
                throw new ArgumentNullException(nameof(value));

            UpdateLastAccessed();
            Data.AddOrUpdate(key, value, (k, oldValue) => value);

            // Keep SharedData in sync for backward compatibility
            SharedData[key] = value;
        }

        /// <summary>
        /// Retrieve typed data with validation
        /// </summary>
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

        /// <summary>
        /// Retrieve typed value data (for value types)
        /// </summary>
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

        /// <summary>
        /// Check if required data exists
        /// </summary>
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

        #endregion

        #region Legacy Properties (for backward compatibility)

        /// <summary>
        /// The queue item being processed
        /// </summary>
        public AutoLoginQueueItem QueueItem { get; set; } = null!;

        /// <summary>
        /// The current subtask being executed
        /// </summary>
        public AutoLoginSubtask Subtask { get; set; } = null!;

        /// <summary>
        /// Progress reporting callback
        /// </summary>
        public Action<int, string>? ProgressCallback { get; set; }

        /// <summary>
        /// Cancellation token for the operation
        /// </summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Additional context data that can be shared between handlers (legacy compatibility)
        /// </summary>
        public Dictionary<string, object> SharedData { get; set; }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            Data.Clear();
            SharedData.Clear();
            _disposed = true;
        }
    }
}
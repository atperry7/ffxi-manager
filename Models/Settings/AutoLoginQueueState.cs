namespace FFXIManager.Models.Settings
{
    /// <summary>
    /// Persistent state for the auto-login queue that survives application restarts
    /// </summary>
    public class AutoLoginQueueState
    {
        /// <summary>
        /// Serialized queue items for persistence
        /// </summary>
        public List<SerializableQueueItem> QueueItems { get; set; } = new();

        /// <summary>
        /// Whether the queue was executing when the application last closed
        /// </summary>
        public bool WasExecuting { get; set; }

        /// <summary>
        /// Whether the queue was paused when the application last closed
        /// </summary>
        public bool WasPaused { get; set; }

        /// <summary>
        /// Original profile that was active before queue execution started
        /// </summary>
        public string? OriginalProfilePath { get; set; }

        /// <summary>
        /// Last time the queue state was saved
        /// </summary>
        public DateTime LastSaved { get; set; } = DateTime.Now;

        /// <summary>
        /// Queue execution statistics
        /// </summary>
        public QueueExecutionStatistics Statistics { get; set; } = new();
    }

    /// <summary>
    /// Serializable representation of a queue item for persistence
    /// </summary>
    public class SerializableQueueItem
    {
        /// <summary>
        /// Unique identifier for the queue item
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Account ID from PlayOnlineMemberAccount
        /// </summary>
        public Guid AccountId { get; set; }

        /// <summary>
        /// Profile file path containing the account
        /// </summary>
        public string ProfilePath { get; set; } = string.Empty;

        /// <summary>
        /// Position in the queue (1-based)
        /// </summary>
        public int Position { get; set; }

        /// <summary>
        /// Status when saved
        /// </summary>
        public AutoLoginQueueStatus Status { get; set; }

        /// <summary>
        /// Start time if the item was started
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// End time if the item was completed
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// Error message if the item failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Status message when saved
        /// </summary>
        public string? StatusMessage { get; set; }

        /// <summary>
        /// Creates a SerializableQueueItem from an AutoLoginQueueItem
        /// </summary>
        public static SerializableQueueItem FromQueueItem(AutoLoginQueueItem item)
        {
            return new SerializableQueueItem
            {
                Id = item.Id,
                AccountId = item.Account.Id,
                ProfilePath = item.Profile.FilePath,
                Position = item.Position,
                Status = item.Status,
                StartTime = item.StartTime,
                EndTime = item.EndTime,
                ErrorMessage = item.ErrorMessage,
                StatusMessage = item.StatusMessage
            };
        }
    }

    /// <summary>
    /// Queue execution statistics for analysis and display
    /// </summary>
    public class QueueExecutionStatistics
    {
        /// <summary>
        /// Total number of queue executions
        /// </summary>
        public int TotalExecutions { get; set; }

        /// <summary>
        /// Total number of successfully completed items
        /// </summary>
        public int TotalCompletedItems { get; set; }

        /// <summary>
        /// Total number of failed items
        /// </summary>
        public int TotalFailedItems { get; set; }

        /// <summary>
        /// Total execution time across all queue runs
        /// </summary>
        public TimeSpan TotalExecutionTime { get; set; }

        /// <summary>
        /// Average time per item
        /// </summary>
        public TimeSpan AverageItemTime { get; set; }

        /// <summary>
        /// Success rate (0.0 to 1.0)
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Last execution start time
        /// </summary>
        public DateTime? LastExecutionStart { get; set; }

        /// <summary>
        /// Last execution end time
        /// </summary>
        public DateTime? LastExecutionEnd { get; set; }

        /// <summary>
        /// Most common failure reasons
        /// </summary>
        public Dictionary<string, int> FailureReasons { get; set; } = new();

        /// <summary>
        /// Updates statistics with a completed queue item
        /// </summary>
        public void UpdateWithCompletedItem(AutoLoginQueueItem item)
        {
            if (item.Status == AutoLoginQueueStatus.Completed)
            {
                TotalCompletedItems++;
                if (item.Duration.HasValue)
                {
                    TotalExecutionTime = TotalExecutionTime.Add(item.Duration.Value);
                    UpdateAverageItemTime();
                }
            }
            else if (item.Status == AutoLoginQueueStatus.Failed)
            {
                TotalFailedItems++;
                if (!string.IsNullOrEmpty(item.ErrorMessage))
                {
                    var reason = item.ErrorMessage.Length > 50
                        ? item.ErrorMessage.Substring(0, 50) + "..."
                        : item.ErrorMessage;

                    if (FailureReasons.ContainsKey(reason))
                        FailureReasons[reason]++;
                    else
                        FailureReasons[reason] = 1;
                }
            }

            UpdateSuccessRate();
        }

        /// <summary>
        /// Updates statistics when a queue execution starts
        /// </summary>
        public void UpdateExecutionStart()
        {
            TotalExecutions++;
            LastExecutionStart = DateTime.Now;
        }

        /// <summary>
        /// Updates statistics when a queue execution ends
        /// </summary>
        public void UpdateExecutionEnd()
        {
            LastExecutionEnd = DateTime.Now;
        }

        private void UpdateAverageItemTime()
        {
            var totalItems = TotalCompletedItems + TotalFailedItems;
            if (totalItems > 0)
            {
                AverageItemTime = TimeSpan.FromTicks(TotalExecutionTime.Ticks / totalItems);
            }
        }

        private void UpdateSuccessRate()
        {
            var totalItems = TotalCompletedItems + TotalFailedItems;
            SuccessRate = totalItems > 0 ? (double)TotalCompletedItems / totalItems : 0.0;
        }
    }
}
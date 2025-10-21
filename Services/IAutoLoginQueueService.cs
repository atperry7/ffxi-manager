using FFXIManager.Models;
using System.Collections.ObjectModel;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for managing the auto-login queue with sequential execution
    /// </summary>
    public interface IAutoLoginQueueService
    {
        #region Properties

        /// <summary>
        /// Observable collection of queue items
        /// </summary>
        ObservableCollection<AutoLoginQueueItem> QueueItems { get; }

        /// <summary>
        /// Whether the queue is currently executing
        /// </summary>
        bool IsExecuting { get; }

        /// <summary>
        /// Whether the queue execution is paused
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// Current execution state of the queue
        /// </summary>
        QueueExecutionState ExecutionState { get; }

        /// <summary>
        /// Message to display during transitions (skip, item changes, etc.)
        /// </summary>
        string TransitioningMessage { get; }

        /// <summary>
        /// Current queue item being processed
        /// </summary>
        AutoLoginQueueItem? CurrentItem { get; }

        /// <summary>
        /// Total number of items in the queue
        /// </summary>
        int TotalItems { get; }

        /// <summary>
        /// Number of completed items
        /// </summary>
        int CompletedItems { get; }

        /// <summary>
        /// Number of failed items
        /// </summary>
        int FailedItems { get; }

        /// <summary>
        /// Number of cancelled/skipped items
        /// </summary>
        int CancelledItems { get; }

        /// <summary>
        /// Total number of processed items (completed + failed + cancelled)
        /// </summary>
        int ProcessedItems { get; }

        /// <summary>
        /// Overall progress percentage (0-100)
        /// </summary>
        int OverallProgress { get; }

        #endregion

        #region Events

        /// <summary>
        /// Raised when queue execution starts
        /// </summary>
        event EventHandler? QueueStarted;

        /// <summary>
        /// Raised when queue execution stops (completed, cancelled, or failed)
        /// </summary>
        event EventHandler<QueueStoppedEventArgs>? QueueStopped;

        /// <summary>
        /// Raised when queue execution is paused
        /// </summary>
        event EventHandler? QueuePaused;

        /// <summary>
        /// Raised when queue execution is resumed
        /// </summary>
        event EventHandler? QueueResumed;

        /// <summary>
        /// Raised when a queue item starts processing
        /// </summary>
        event EventHandler<AutoLoginQueueItemEventArgs>? ItemStarted;

        /// <summary>
        /// Raised when a queue item completes successfully
        /// </summary>
        event EventHandler<AutoLoginQueueItemEventArgs>? ItemCompleted;

        /// <summary>
        /// Raised when a queue item fails
        /// </summary>
        event EventHandler<AutoLoginQueueItemEventArgs>? ItemFailed;

        /// <summary>
        /// Raised when a queue item's progress updates
        /// </summary>
        event EventHandler<AutoLoginQueueItemEventArgs>? ItemProgressUpdated;

        /// <summary>
        /// Raised when a profile is swapped during queue execution
        /// </summary>
        event EventHandler<ProfileSwappedEventArgs>? ProfileSwapped;

        #endregion

        #region Queue Management

        /// <summary>
        /// Adds an account to the queue
        /// </summary>
        /// <param name="account">PlayOnline Member Account to add</param>
        /// <param name="profile">Profile containing the account</param>
        /// <returns>The created queue item</returns>
        Task<AutoLoginQueueItem> AddToQueueAsync(PlayOnlineMemberAccount account, ProfileInfo profile);

        /// <summary>
        /// Removes an item from the queue
        /// </summary>
        /// <param name="item">Queue item to remove</param>
        /// <returns>True if removed successfully</returns>
        Task<bool> RemoveFromQueueAsync(AutoLoginQueueItem item);

        /// <summary>
        /// Clears all items from the queue
        /// </summary>
        Task ClearQueueAsync();

        /// <summary>
        /// Moves a queue item to a new position
        /// </summary>
        /// <param name="item">Item to move</param>
        /// <param name="newPosition">New position (1-based)</param>
        /// <returns>True if moved successfully</returns>
        Task<bool> MoveItemAsync(AutoLoginQueueItem item, int newPosition);

        /// <summary>
        /// Reorders queue items
        /// </summary>
        /// <param name="newOrder">New order of items</param>
        Task ReorderQueueAsync(IList<AutoLoginQueueItem> newOrder);

        #endregion

        #region Execution Control

        /// <summary>
        /// Starts queue execution
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        Task StartQueueAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops queue execution immediately
        /// </summary>
        Task StopQueueAsync();

        /// <summary>
        /// Pauses queue execution (current item continues, but no new items start)
        /// </summary>
        Task PauseQueueAsync();

        /// <summary>
        /// Resumes paused queue execution
        /// </summary>
        Task ResumeQueueAsync();

        /// <summary>
        /// Cancels current item and skips to next
        /// </summary>
        Task SkipCurrentItemAsync();

        /// <summary>
        /// Retries a failed queue item
        /// </summary>
        /// <param name="item">Item to retry</param>
        Task RetryItemAsync(AutoLoginQueueItem item);

        /// <summary>
        /// Resets queue items back to pending state for re-execution
        /// </summary>
        Task ResetQueueAsync();

        #endregion

        #region Persistence

        /// <summary>
        /// Saves the current queue state to settings
        /// </summary>
        Task SaveQueueStateAsync();

        /// <summary>
        /// Loads queue state from settings
        /// </summary>
        Task LoadQueueStateAsync();

        /// <summary>
        /// Gets queue statistics
        /// </summary>
        QueueStatistics GetStatistics();

        #endregion

        #region Configuration

        /// <summary>
        /// Gets or sets whether to auto-save queue state
        /// </summary>
        bool AutoSaveQueueState { get; set; }

        /// <summary>
        /// Gets or sets whether to continue queue after failure
        /// </summary>
        bool ContinueOnFailure { get; set; }

        /// <summary>
        /// Gets or sets delay between queue items in milliseconds
        /// </summary>
        int DelayBetweenItems { get; set; }

        /// <summary>
        /// Gets or sets timeout for each login step in seconds
        /// </summary>
        int StepTimeoutSeconds { get; set; }

        #endregion

        // No explicit synchronization API required when account instances are canonical
    }

    #region Event Args

    /// <summary>
    /// Event arguments for queue stopped event
    /// </summary>
    public class QueueStoppedEventArgs : EventArgs
    {
        public QueueStoppedEventArgs(QueueStopReason reason, string? message = null)
        {
            Reason = reason;
            Message = message;
        }

        public QueueStopReason Reason { get; }
        public string? Message { get; }
    }

    /// <summary>
    /// Event arguments for queue item events
    /// </summary>
    public class AutoLoginQueueItemEventArgs : EventArgs
    {
        public AutoLoginQueueItemEventArgs(AutoLoginQueueItem item, string? message = null)
        {
            Item = item;
            Message = message;
        }

        public AutoLoginQueueItem Item { get; }
        public string? Message { get; }
    }

    /// <summary>
    /// Event arguments for profile swapped events
    /// </summary>
    public class ProfileSwappedEventArgs : EventArgs
    {
        public ProfileSwappedEventArgs(ProfileInfo fromProfile, ProfileInfo toProfile)
        {
            FromProfile = fromProfile;
            ToProfile = toProfile;
        }

        public ProfileInfo FromProfile { get; }
        public ProfileInfo ToProfile { get; }
    }

    /// <summary>
    /// Reason why queue execution stopped
    /// </summary>
    public enum QueueStopReason
    {
        Completed,
        Cancelled,
        Failed,
        UserRequested
    }

    /// <summary>
    /// Queue execution statistics
    /// </summary>
    public class QueueStatistics
    {
        public int TotalItems { get; set; }
        public int CompletedItems { get; set; }
        public int FailedItems { get; set; }
        public int PendingItems { get; set; }
        public TimeSpan TotalExecutionTime { get; set; }
        public TimeSpan AverageItemTime { get; set; }
        public double SuccessRate { get; set; }
        public DateTime? LastExecutionStart { get; set; }
        public DateTime? LastExecutionEnd { get; set; }
    }

    #endregion
}

using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for orchestrating queue execution flow and control operations
    /// </summary>
    public interface IQueueExecutionOrchestrator
    {
        #region Configuration

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

        /// <summary>
        /// Gets or sets whether to restore original profile after queue completion
        /// </summary>
        bool RestoreOriginalProfileAfterQueue { get; set; }

        #endregion

        #region Execution Control

        /// <summary>
        /// Starts queue execution with the provided context
        /// </summary>
        /// <param name="context">Execution context containing dependencies and state</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task StartExecutionAsync(QueueExecutionContext context, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops queue execution immediately
        /// </summary>
        Task StopExecutionAsync();

        /// <summary>
        /// Pauses queue execution (current item continues, but no new items start)
        /// </summary>
        Task PauseExecutionAsync();

        /// <summary>
        /// Resumes paused queue execution
        /// </summary>
        Task ResumeExecutionAsync();

        /// <summary>
        /// Cancels current item and skips to next
        /// </summary>
        /// <param name="currentItem">Current queue item to skip</param>
        Task SkipCurrentItemAsync(AutoLoginQueueItem currentItem);

        #endregion

        #region Events

        /// <summary>
        /// Raised when queue execution starts
        /// </summary>
        event EventHandler? ExecutionStarted;

        /// <summary>
        /// Raised when queue execution stops (completed, cancelled, or failed)
        /// </summary>
        event EventHandler<QueueStoppedEventArgs>? ExecutionStopped;

        /// <summary>
        /// Raised when queue execution is paused
        /// </summary>
        event EventHandler? ExecutionPaused;

        /// <summary>
        /// Raised when queue execution is resumed
        /// </summary>
        event EventHandler? ExecutionResumed;

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
        /// Raised when a profile is swapped during queue execution
        /// </summary>
        event EventHandler<ProfileSwappedEventArgs>? ProfileSwapped;

        #endregion
    }

    /// <summary>
    /// Context object containing all dependencies needed for queue execution
    /// </summary>
    public class QueueExecutionContext
    {
        public required IQueueCollectionManager CollectionManager { get; init; }
        public required IQueueStateMachine StateMachine { get; init; }
        public required IQueueStatisticsService StatisticsService { get; init; }
        public required IQueuePersistenceService PersistenceService { get; init; }
        public required IAutoLoginTaskExecutor TaskExecutor { get; init; }
        public required IProfileService ProfileService { get; init; }
        public required ILoggingService LoggingService { get; init; }
        public required string? OriginalProfilePath { get; set; }
    }
}
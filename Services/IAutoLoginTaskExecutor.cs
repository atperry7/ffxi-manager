using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for executing auto-login tasks with subtask granularity.
    /// Separates task execution logic from queue management for better testability and maintainability.
    /// </summary>
    public interface IAutoLoginTaskExecutor
    {
        #region Events

        /// <summary>
        /// Raised when a task starts execution
        /// </summary>
        event EventHandler<AutoLoginTaskEventArgs>? TaskStarted;

        /// <summary>
        /// Raised when a task completes successfully
        /// </summary>
        event EventHandler<AutoLoginTaskEventArgs>? TaskCompleted;

        /// <summary>
        /// Raised when a task fails with an error
        /// </summary>
        event EventHandler<AutoLoginTaskEventArgs>? TaskFailed;

        /// <summary>
        /// Raised when a task's progress updates
        /// </summary>
        event EventHandler<AutoLoginTaskEventArgs>? TaskProgressUpdated;

        /// <summary>
        /// Raised when a subtask starts execution
        /// </summary>
        event EventHandler<AutoLoginSubtaskEventArgs>? SubtaskStarted;

        /// <summary>
        /// Raised when a subtask completes successfully
        /// </summary>
        event EventHandler<AutoLoginSubtaskEventArgs>? SubtaskCompleted;

        /// <summary>
        /// Raised when a subtask fails with an error
        /// </summary>
        event EventHandler<AutoLoginSubtaskEventArgs>? SubtaskFailed;

        /// <summary>
        /// Raised when a subtask's progress updates
        /// </summary>
        event EventHandler<AutoLoginSubtaskEventArgs>? SubtaskProgressUpdated;

        #endregion

        #region Execution

        /// <summary>
        /// Executes an auto-login task for the specified queue item
        /// </summary>
        /// <param name="queueItem">Queue item containing account and profile information</param>
        /// <param name="cancellationToken">Cancellation token for stopping execution</param>
        /// <returns>Task representing the async execution</returns>
        Task ExecuteAsync(AutoLoginQueueItem queueItem, CancellationToken cancellationToken = default);

        /// <summary>
        /// Pauses the current task execution (if supported by current subtask)
        /// </summary>
        /// <returns>True if pause was successful, false if not supported</returns>
        Task<bool> PauseCurrentTaskAsync();

        /// <summary>
        /// Resumes a paused task execution
        /// </summary>
        /// <returns>True if resume was successful, false if not paused</returns>
        Task<bool> ResumeCurrentTaskAsync();

        /// <summary>
        /// Skips the current subtask and moves to the next one
        /// </summary>
        /// <returns>True if skip was successful, false if not possible</returns>
        Task<bool> SkipCurrentSubtaskAsync();

        #endregion

        #region Configuration

        /// <summary>
        /// Gets or sets timeout for each subtask execution in seconds
        /// </summary>
        int SubtaskTimeoutSeconds { get; set; }

        /// <summary>
        /// Gets or sets delay between subtasks in milliseconds
        /// </summary>
        int DelayBetweenSubtasks { get; set; }

        /// <summary>
        /// Gets or sets whether to continue task execution if a subtask fails
        /// </summary>
        bool ContinueOnSubtaskFailure { get; set; }

        #endregion
    }

    #region Event Args

    /// <summary>
    /// Event arguments for task-level events
    /// </summary>
    public class AutoLoginTaskEventArgs : EventArgs
    {
        public AutoLoginTaskEventArgs(AutoLoginQueueItem queueItem, AutoLoginTask task, string? message = null)
        {
            QueueItem = queueItem;
            Task = task;
            Message = message;
        }

        public AutoLoginQueueItem QueueItem { get; }
        public AutoLoginTask Task { get; }
        public string? Message { get; }
    }

    /// <summary>
    /// Event arguments for subtask-level events
    /// </summary>
    public class AutoLoginSubtaskEventArgs : EventArgs
    {
        public AutoLoginSubtaskEventArgs(AutoLoginQueueItem queueItem, AutoLoginTask task, AutoLoginSubtask subtask, string? message = null)
        {
            QueueItem = queueItem;
            Task = task;
            Subtask = subtask;
            Message = message;
        }

        public AutoLoginQueueItem QueueItem { get; }
        public AutoLoginTask Task { get; }
        public AutoLoginSubtask Subtask { get; }
        public string? Message { get; }
    }

    #endregion
}
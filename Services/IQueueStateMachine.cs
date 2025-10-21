using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for managing queue execution state transitions and validation
    /// </summary>
    public interface IQueueStateMachine
    {
        #region Properties

        /// <summary>
        /// Current execution state of the queue
        /// </summary>
        QueueExecutionState ExecutionState { get; }

        /// <summary>
        /// Whether the queue is currently executing (legacy property for backward compatibility)
        /// </summary>
        bool IsExecuting { get; }

        /// <summary>
        /// Whether the queue execution is paused (legacy property for backward compatibility)
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// Message to display during transitions (skip, item changes, etc.)
        /// </summary>
        string TransitioningMessage { get; }

        /// <summary>
        /// Current queue item being processed
        /// </summary>
        AutoLoginQueueItem? CurrentItem { get; set; }

        #endregion

        #region State Transitions

        /// <summary>
        /// Attempts to transition to a new execution state
        /// </summary>
        /// <param name="newState">The desired new state</param>
        /// <param name="message">Optional message describing the transition</param>
        /// <returns>True if the transition was successful</returns>
        bool TryTransitionTo(QueueExecutionState newState, string message = "");

        /// <summary>
        /// Validates if a state transition is allowed
        /// </summary>
        /// <param name="newState">The desired new state</param>
        /// <returns>True if the transition is valid</returns>
        bool CanTransitionTo(QueueExecutionState newState);

        /// <summary>
        /// Forces a transition to the specified state (use with caution)
        /// </summary>
        /// <param name="newState">The new state to force</param>
        /// <param name="message">Optional message describing the transition</param>
        void ForceTransitionTo(QueueExecutionState newState, string message = "");

        #endregion

        #region State Queries

        /// <summary>
        /// Checks if the queue can start execution
        /// </summary>
        bool CanStart();

        /// <summary>
        /// Checks if the queue can stop execution
        /// </summary>
        bool CanStop();

        /// <summary>
        /// Checks if the queue can be paused
        /// </summary>
        bool CanPause();

        /// <summary>
        /// Checks if the queue can be resumed
        /// </summary>
        bool CanResume();

        /// <summary>
        /// Checks if the current item can be skipped
        /// </summary>
        bool CanSkipCurrentItem();

        #endregion

        #region Events

        /// <summary>
        /// Raised when the execution state changes
        /// </summary>
        event EventHandler<StateTransitionEventArgs>? StateChanged;

        #endregion
    }

    /// <summary>
    /// Event arguments for state transition events
    /// </summary>
    public class StateTransitionEventArgs : EventArgs
    {
        public StateTransitionEventArgs(QueueExecutionState oldState, QueueExecutionState newState, string message)
        {
            OldState = oldState;
            NewState = newState;
            Message = message;
            Timestamp = DateTime.Now;
        }

        public QueueExecutionState OldState { get; }
        public QueueExecutionState NewState { get; }
        public string Message { get; }
        public DateTime Timestamp { get; }
    }
}
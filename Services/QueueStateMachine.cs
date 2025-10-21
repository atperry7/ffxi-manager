using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Implementation of queue state machine for managing execution state transitions
    /// </summary>
    public class QueueStateMachine : IQueueStateMachine
    {
        private readonly ILoggingService _loggingService;
        private readonly object _lockObject = new();

        public QueueStateMachine(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            ExecutionState = QueueExecutionState.Idle;
            TransitioningMessage = string.Empty;
        }

        #region Properties

        public QueueExecutionState ExecutionState { get; private set; }

        public bool IsExecuting => ExecutionState is QueueExecutionState.Starting or QueueExecutionState.Processing
            or QueueExecutionState.Transitioning or QueueExecutionState.Stopping;

        public bool IsPaused => ExecutionState == QueueExecutionState.Paused;

        public string TransitioningMessage { get; private set; }

        public AutoLoginQueueItem? CurrentItem { get; set; }

        #endregion

        #region Events

        public event EventHandler<StateTransitionEventArgs>? StateChanged;

        #endregion

        #region State Transitions

        public bool TryTransitionTo(QueueExecutionState newState, string message = "")
        {
            lock (_lockObject)
            {
                if (!CanTransitionTo(newState))
                {
                    return false;
                }

                var oldState = ExecutionState;
                ExecutionState = newState;
                TransitioningMessage = message;

                if (oldState != newState)
                {
                    _loggingService.LogInfoAsync($"Queue state transition: {oldState} → {newState}" +
                        (string.IsNullOrEmpty(message) ? "" : $" ({message})"));

                    StateChanged?.Invoke(this, new StateTransitionEventArgs(oldState, newState, message));
                }

                return true;
            }
        }

        public bool CanTransitionTo(QueueExecutionState newState)
        {
            return ExecutionState switch
            {
                QueueExecutionState.Idle => newState is QueueExecutionState.Starting or QueueExecutionState.Completed,
                QueueExecutionState.Starting => newState is QueueExecutionState.Processing or QueueExecutionState.Stopping or QueueExecutionState.Idle,
                QueueExecutionState.Processing => newState is QueueExecutionState.Transitioning or QueueExecutionState.Paused or QueueExecutionState.Stopping or QueueExecutionState.Completed,
                QueueExecutionState.Transitioning => newState is QueueExecutionState.Processing or QueueExecutionState.Stopping or QueueExecutionState.Completed,
                QueueExecutionState.Paused => newState is QueueExecutionState.Processing or QueueExecutionState.Stopping,
                QueueExecutionState.Stopping => newState is QueueExecutionState.Idle or QueueExecutionState.Completed,
                QueueExecutionState.Completed => newState is QueueExecutionState.Idle or QueueExecutionState.Starting,
                _ => false
            };
        }

        public void ForceTransitionTo(QueueExecutionState newState, string message = "")
        {
            lock (_lockObject)
            {
                var oldState = ExecutionState;
                ExecutionState = newState;
                TransitioningMessage = message;

                _loggingService.LogWarningAsync($"Forced queue state transition: {oldState} → {newState}" +
                    (string.IsNullOrEmpty(message) ? "" : $" ({message})"));

                StateChanged?.Invoke(this, new StateTransitionEventArgs(oldState, newState, message));
            }
        }

        #endregion

        #region State Queries

        public bool CanStart()
        {
            return ExecutionState == QueueExecutionState.Idle;
        }

        public bool CanStop()
        {
            return IsExecuting;
        }

        public bool CanPause()
        {
            return ExecutionState == QueueExecutionState.Processing;
        }

        public bool CanResume()
        {
            return ExecutionState == QueueExecutionState.Paused;
        }

        public bool CanSkipCurrentItem()
        {
            return CurrentItem != null &&
                   (ExecutionState == QueueExecutionState.Processing || ExecutionState == QueueExecutionState.Paused);
        }

        #endregion
    }
}
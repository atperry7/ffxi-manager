using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents a complete auto-login task containing a collection of subtasks.
    /// Provides task-level progress tracking and state management.
    /// </summary>
    public class AutoLoginTask : INotifyPropertyChanged
    {
        private AutoLoginTaskStatus _status = AutoLoginTaskStatus.Pending;
        private AutoLoginSubtask? _currentSubtask;
        private string _statusMessage = string.Empty;
        private DateTime? _startTime;
        private DateTime? _endTime;
        private string _errorMessage = string.Empty;
        private DateTime _lastProgressUpdate = DateTime.MinValue;
        private const int ProgressUpdateThrottleMs = 250; // Minimum 250ms between progress updates

        /// <summary>
        /// Unique identifier for this task
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Display name for this task
        /// </summary>
        public string Name { get; set; } = "Auto-Login Task";

        /// <summary>
        /// Detailed description of what this task accomplishes
        /// </summary>
        public string Description { get; set; } = "Complete auto-login sequence for FFXI account";

        /// <summary>
        /// Collection of subtasks that comprise this login task
        /// </summary>
        public List<AutoLoginSubtask> Subtasks { get; set; } = new();

        /// <summary>
        /// Current status of the task
        /// </summary>
        public AutoLoginTaskStatus Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                {
                    OnPropertyChanged(nameof(StatusDisplay));
                    OnPropertyChanged(nameof(IsActive));
                    OnPropertyChanged(nameof(IsCompleted));
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        /// <summary>
        /// Current subtask being executed
        /// </summary>
        public AutoLoginSubtask? CurrentSubtask
        {
            get => _currentSubtask;
            set
            {
                if (SetProperty(ref _currentSubtask, value))
                {
                    OnPropertyChanged(nameof(CurrentSubtaskDisplay));
                    OnPropertyChanged(nameof(Progress));

                    // Update StatusMessage to show current executing step
                    if (value != null && Status == AutoLoginTaskStatus.InProgress)
                    {
                        StatusMessage = value.Name;
                    }

                    // Reset progress update throttle when switching subtasks
                    // This ensures immediate UI update on subtask transitions
                    _lastProgressUpdate = DateTime.MinValue;
                }
            }
        }

        /// <summary>
        /// Current status message
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Time when task execution started
        /// </summary>
        public DateTime? StartTime
        {
            get => _startTime;
            set
            {
                if (SetProperty(ref _startTime, value))
                {
                    OnPropertyChanged(nameof(Duration));
                }
            }
        }

        /// <summary>
        /// Time when task execution completed
        /// </summary>
        public DateTime? EndTime
        {
            get => _endTime;
            set
            {
                if (SetProperty(ref _endTime, value))
                {
                    OnPropertyChanged(nameof(Duration));
                }
            }
        }

        /// <summary>
        /// Error message if task failed
        /// </summary>
        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        #region Computed Properties

        /// <summary>
        /// Whether this task is currently being executed
        /// </summary>
        public bool IsActive => Status == AutoLoginTaskStatus.InProgress;

        /// <summary>
        /// Whether this task has completed successfully
        /// </summary>
        public bool IsCompleted => Status == AutoLoginTaskStatus.Completed;

        /// <summary>
        /// Whether this task has an error
        /// </summary>
        public bool HasError => Status == AutoLoginTaskStatus.Failed || !string.IsNullOrEmpty(ErrorMessage);

        /// <summary>
        /// Status display text
        /// </summary>
        public string StatusDisplay => Status switch
        {
            AutoLoginTaskStatus.Pending => "Pending",
            AutoLoginTaskStatus.InProgress => "In Progress",
            AutoLoginTaskStatus.Paused => "Paused",
            AutoLoginTaskStatus.Completed => "Completed",
            AutoLoginTaskStatus.Failed => "Failed",
            AutoLoginTaskStatus.Cancelled => "Cancelled",
            _ => "Unknown"
        };

        /// <summary>
        /// Current subtask display text
        /// </summary>
        public string CurrentSubtaskDisplay => CurrentSubtask?.Name ?? "Waiting";

        /// <summary>
        /// Overall task progress percentage (0-100)
        /// </summary>
        public int Progress
        {
            get
            {
                if (Status == AutoLoginTaskStatus.Completed) return 100;
                if (Status == AutoLoginTaskStatus.Failed || Status == AutoLoginTaskStatus.Cancelled) return 0;
                if (Subtasks.Count == 0) return 0;

                var completedSubtasks = Subtasks.Count(s => s.Status == AutoLoginSubtaskStatus.Completed);
                var currentSubtaskProgress = CurrentSubtask?.Progress ?? 0;

                // Calculate: (completed subtasks + current subtask progress) / total subtasks * 100
                var totalProgress = completedSubtasks + (currentSubtaskProgress / 100.0);
                return (int)(totalProgress / Subtasks.Count * 100);
            }
        }

        /// <summary>
        /// Duration of task execution
        /// </summary>
        public TimeSpan? Duration
        {
            get
            {
                if (StartTime == null) return null;

                // Use EndTime if available (task completed), otherwise use current time only for active tasks
                var endTime = EndTime ?? (IsActive && EndTime == null ? DateTime.UtcNow : null);
                if (endTime == null) return null;
                var diff = endTime.Value - StartTime.Value;
                return diff < TimeSpan.Zero ? TimeSpan.Zero : diff;
            }
        }

        /// <summary>
        /// Duration display text
        /// </summary>
        public string DurationDisplay
        {
            get
            {
                var duration = Duration;
                if (duration == null) return "-";

                return duration.Value.TotalMinutes >= 1
                    ? $"{duration.Value.Minutes:D2}:{duration.Value.Seconds:D2}"
                    : $"{duration.Value.Seconds}s";
            }
        }

        /// <summary>
        /// Number of completed subtasks
        /// </summary>
        public int CompletedSubtasks => Subtasks.Count(s => s.Status == AutoLoginSubtaskStatus.Completed);

        /// <summary>
        /// Number of failed subtasks
        /// </summary>
        public int FailedSubtasks => Subtasks.Count(s => s.Status == AutoLoginSubtaskStatus.Failed);

        /// <summary>
        /// Total number of subtasks
        /// </summary>
        public int TotalSubtasks => Subtasks.Count;

        #endregion

        #region Methods

        /// <summary>
        /// Adds a subtask to the task
        /// </summary>
        public void AddSubtask(AutoLoginSubtask subtask)
        {
            if (subtask == null) throw new ArgumentNullException(nameof(subtask));

            Subtasks.Add(subtask);
            subtask.PropertyChanged += OnSubtaskPropertyChanged;
            OnPropertyChanged(nameof(TotalSubtasks));
            OnPropertyChanged(nameof(Progress));
        }

        /// <summary>
        /// Removes a subtask from the task
        /// </summary>
        public bool RemoveSubtask(AutoLoginSubtask subtask)
        {
            if (subtask == null) return false;

            if (Subtasks.Remove(subtask))
            {
                subtask.PropertyChanged -= OnSubtaskPropertyChanged;
                OnPropertyChanged(nameof(TotalSubtasks));
                OnPropertyChanged(nameof(Progress));
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the next pending subtask
        /// </summary>
        public AutoLoginSubtask? GetNextPendingSubtask()
        {
            return Subtasks.FirstOrDefault(s => s.Status == AutoLoginSubtaskStatus.Pending);
        }

        /// <summary>
        /// Marks the task as started
        /// </summary>
        public void Start()
        {
            Status = AutoLoginTaskStatus.InProgress;
            StartTime = DateTime.UtcNow;
            StatusMessage = "Task started";
        }

        /// <summary>
        /// Marks the task as completed
        /// </summary>
        public void Complete()
        {
            Status = AutoLoginTaskStatus.Completed;
            EndTime = DateTime.UtcNow;
            CurrentSubtask = null;
            StatusMessage = "Task completed successfully";
        }

        /// <summary>
        /// Marks the task as failed with an error message
        /// </summary>
        public void Fail(string errorMessage)
        {
            Status = AutoLoginTaskStatus.Failed;
            EndTime = DateTime.UtcNow;
            ErrorMessage = errorMessage;
            StatusMessage = $"Task failed: {errorMessage}";
        }

        /// <summary>
        /// Pauses the task execution
        /// </summary>
        public void Pause()
        {
            if (Status == AutoLoginTaskStatus.InProgress)
            {
                Status = AutoLoginTaskStatus.Paused;
                StatusMessage = "Task paused";
            }
        }

        /// <summary>
        /// Resumes paused task execution
        /// </summary>
        public void Resume()
        {
            if (Status == AutoLoginTaskStatus.Paused)
            {
                Status = AutoLoginTaskStatus.InProgress;
                StatusMessage = "Task resumed";
            }
        }

        /// <summary>
        /// Cancels task execution
        /// </summary>
        public void Cancel()
        {
            Status = AutoLoginTaskStatus.Cancelled;
            EndTime = DateTime.UtcNow;
            StatusMessage = "Task cancelled";
        }

        /// <summary>
        /// Resets the task to pending state
        /// </summary>
        public void Reset()
        {
            Status = AutoLoginTaskStatus.Pending;
            CurrentSubtask = null;
            StatusMessage = string.Empty;
            StartTime = null;
            EndTime = null;
            ErrorMessage = string.Empty;

            // Reset all subtasks
            foreach (var subtask in Subtasks)
            {
                subtask.Reset();
            }
        }

        /// <summary>
        /// Refreshes duration display for real-time updates
        /// </summary>
        public void RefreshDurationDisplay()
        {
            OnPropertyChanged(nameof(Duration));
            OnPropertyChanged(nameof(DurationDisplay));
        }

        #endregion

        #region Event Handling

        private void OnSubtaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Update task progress when subtask progress changes
            if (e.PropertyName == nameof(AutoLoginSubtask.Status) ||
                e.PropertyName == nameof(AutoLoginSubtask.Progress))
            {
                // Throttle progress updates to smooth UI updates (max once per 250ms)
                var now = DateTime.UtcNow;
                var timeSinceLastUpdate = (now - _lastProgressUpdate).TotalMilliseconds;

                // Always update on Status changes (completion, failure, etc.)
                // Throttle Progress updates to reduce UI jumpiness
                var isStatusChange = e.PropertyName == nameof(AutoLoginSubtask.Status);
                var shouldUpdateProgress = isStatusChange || timeSinceLastUpdate >= ProgressUpdateThrottleMs;

                if (shouldUpdateProgress)
                {
                    _lastProgressUpdate = now;
                    OnPropertyChanged(nameof(Progress));
                    OnPropertyChanged(nameof(CompletedSubtasks));
                    OnPropertyChanged(nameof(FailedSubtasks));
                }
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Clean up event subscriptions
        /// </summary>
        public void Cleanup()
        {
            foreach (var subtask in Subtasks)
            {
                subtask.PropertyChanged -= OnSubtaskPropertyChanged;
                subtask.Cleanup();
            }
        }

        #endregion
    }

    /// <summary>
    /// Status of an auto-login task
    /// </summary>
    public enum AutoLoginTaskStatus
    {
        /// <summary>
        /// Task is waiting to be executed
        /// </summary>
        Pending,

        /// <summary>
        /// Task is currently being executed
        /// </summary>
        InProgress,

        /// <summary>
        /// Task execution has been paused
        /// </summary>
        Paused,

        /// <summary>
        /// Task has completed successfully
        /// </summary>
        Completed,

        /// <summary>
        /// Task execution failed with an error
        /// </summary>
        Failed,

        /// <summary>
        /// Task execution was cancelled
        /// </summary>
        Cancelled
    }
}

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents an individual subtask within an auto-login task.
    /// Each subtask corresponds to a specific atomic operation in the login sequence.
    /// </summary>
    public class AutoLoginSubtask : INotifyPropertyChanged
    {
        private AutoLoginSubtaskStatus _status = AutoLoginSubtaskStatus.Pending;
        private int _progress;
        private string _statusMessage = string.Empty;
        private DateTime? _startTime;
        private DateTime? _endTime;
        private string _errorMessage = string.Empty;

        /// <summary>
        /// Unique identifier for this subtask
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Name of the subtask for display purposes
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Detailed description of what this subtask accomplishes
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The login task step this subtask represents
        /// </summary>
        public LoginTaskStep TaskStep { get; set; } = LoginTaskStep.None;

        /// <summary>
        /// Order of execution within the parent task
        /// </summary>
        public int ExecutionOrder { get; set; }

        /// <summary>
        /// Whether this subtask can be skipped if it fails
        /// </summary>
        public bool IsSkippable { get; set; } = false;

        /// <summary>
        /// Estimated duration for this subtask in seconds
        /// </summary>
        public int EstimatedDurationSeconds { get; set; } = 5;

        /// <summary>
        /// Current status of the subtask
        /// </summary>
        public AutoLoginSubtaskStatus Status
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
                    OnPropertyChanged(nameof(CanRetry));
                }
            }
        }

        /// <summary>
        /// Progress of this subtask (0-100)
        /// </summary>
        public int Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, Math.Clamp(value, 0, 100));
        }

        /// <summary>
        /// Current status message for this subtask
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Time when subtask execution started
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
        /// Time when subtask execution completed
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
        /// Error message if subtask failed
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

        /// <summary>
        /// Number of retry attempts made for this subtask
        /// </summary>
        public int RetryAttempts { get; set; }

        /// <summary>
        /// Maximum number of retry attempts allowed
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        #region Computed Properties

        /// <summary>
        /// Whether this subtask is currently being executed
        /// </summary>
        public bool IsActive => Status == AutoLoginSubtaskStatus.InProgress;

        /// <summary>
        /// Whether this subtask has completed successfully
        /// </summary>
        public bool IsCompleted => Status == AutoLoginSubtaskStatus.Completed;

        /// <summary>
        /// Whether this subtask has an error
        /// </summary>
        public bool HasError => Status == AutoLoginSubtaskStatus.Failed || !string.IsNullOrEmpty(ErrorMessage);

        /// <summary>
        /// Whether this subtask can be retried
        /// </summary>
        public bool CanRetry => Status == AutoLoginSubtaskStatus.Failed && RetryAttempts < MaxRetryAttempts;

        /// <summary>
        /// Status display text
        /// </summary>
        public string StatusDisplay => Status switch
        {
            AutoLoginSubtaskStatus.Pending => "Pending",
            AutoLoginSubtaskStatus.InProgress => "In Progress",
            AutoLoginSubtaskStatus.Completed => "Completed",
            AutoLoginSubtaskStatus.Failed => "Failed",
            AutoLoginSubtaskStatus.Skipped => "Skipped",
            AutoLoginSubtaskStatus.Cancelled => "Cancelled",
            _ => "Unknown"
        };

        /// <summary>
        /// Duration of subtask execution
        /// </summary>
        public TimeSpan? Duration
        {
            get
            {
                if (StartTime == null) return null;

                // Use EndTime if available (subtask completed), otherwise use current time only for active subtasks
                var endTime = EndTime ?? (IsActive && EndTime == null ? DateTime.Now : null);
                return endTime?.Subtract(StartTime.Value);
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
        /// Progress display text with percentage
        /// </summary>
        public string ProgressDisplay => $"{Progress}%";

        #endregion

        #region Methods

        /// <summary>
        /// Marks the subtask as started
        /// </summary>
        public void Start()
        {
            Status = AutoLoginSubtaskStatus.InProgress;
            StartTime = DateTime.Now;
            Progress = 0;
            StatusMessage = $"Starting {Name}";
        }

        /// <summary>
        /// Updates the progress of the subtask
        /// </summary>
        /// <param name="progress">Progress percentage (0-100)</param>
        /// <param name="message">Optional status message</param>
        public void UpdateProgress(int progress, string? message = null)
        {
            Progress = progress;
            if (!string.IsNullOrEmpty(message))
            {
                StatusMessage = message;
            }
        }

        /// <summary>
        /// Marks the subtask as completed
        /// </summary>
        public void Complete()
        {
            Status = AutoLoginSubtaskStatus.Completed;
            Progress = 100;
            EndTime = DateTime.Now;
            StatusMessage = $"{Name} completed";
        }

        /// <summary>
        /// Marks the subtask as failed with an error message
        /// </summary>
        /// <param name="errorMessage">Error message describing the failure</param>
        public void Fail(string errorMessage)
        {
            Status = AutoLoginSubtaskStatus.Failed;
            EndTime = DateTime.Now;
            ErrorMessage = errorMessage;
            StatusMessage = $"{Name} failed: {errorMessage}";
        }

        /// <summary>
        /// Marks the subtask as skipped
        /// </summary>
        /// <param name="reason">Reason for skipping</param>
        public void Skip(string? reason = null)
        {
            Status = AutoLoginSubtaskStatus.Skipped;
            EndTime = DateTime.Now;
            StatusMessage = $"{Name} skipped" + (string.IsNullOrEmpty(reason) ? "" : $": {reason}");
        }

        /// <summary>
        /// Cancels the subtask execution
        /// </summary>
        public void Cancel()
        {
            Status = AutoLoginSubtaskStatus.Cancelled;
            EndTime = DateTime.Now;
            StatusMessage = $"{Name} cancelled";
        }

        /// <summary>
        /// Resets the subtask to pending state for retry
        /// </summary>
        public void Reset()
        {
            Status = AutoLoginSubtaskStatus.Pending;
            Progress = 0;
            StatusMessage = string.Empty;
            StartTime = null;
            EndTime = null;
            ErrorMessage = string.Empty;
        }

        /// <summary>
        /// Prepares the subtask for retry
        /// </summary>
        public void PrepareForRetry()
        {
            if (Status == AutoLoginSubtaskStatus.Failed)
            {
                RetryAttempts++;
                Reset();
                StatusMessage = $"Retrying {Name} (attempt {RetryAttempts + 1}/{MaxRetryAttempts + 1})";
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

        #region Factory Methods

        /// <summary>
        /// Creates a subtask from a LoginTaskStep
        /// </summary>
        public static AutoLoginSubtask FromLoginTaskStep(LoginTaskStep step)
        {
            return new AutoLoginSubtask
            {
                TaskStep = step,
                Name = step.GetShortDisplayName(),
                Description = step.GetDisplayName(),
                EstimatedDurationSeconds = step.GetEstimatedDurationSeconds(),
                IsSkippable = step == LoginTaskStep.OTPEntry, // OTP might not be required
                ExecutionOrder = (int)step
            };
        }

        /// <summary>
        /// Creates a collection of subtasks from the main login task steps
        /// </summary>
        public static List<AutoLoginSubtask> CreateStandardLoginSubtasks()
        {
            var subtasks = new List<AutoLoginSubtask>();
            var mainSteps = LoginTaskStepExtensions.GetMainSteps();

            foreach (var step in mainSteps)
            {
                subtasks.Add(FromLoginTaskStep(step));

                // Add Windower sub-tasks if this is the LaunchWindower step
                if (step == LoginTaskStep.LaunchWindower)
                {
                    var windowerSubTasks = LoginTaskStepExtensions.GetWindowerSubTasks();
                    foreach (var subStep in windowerSubTasks)
                    {
                        subtasks.Add(FromLoginTaskStep(subStep));
                    }
                }
            }

            return subtasks;
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
        /// Clean up any resources
        /// </summary>
        public void Cleanup()
        {
            // Currently no resources to clean up, but maintaining pattern for future use
        }

        #endregion
    }

    /// <summary>
    /// Status of an auto-login subtask
    /// </summary>
    public enum AutoLoginSubtaskStatus
    {
        /// <summary>
        /// Subtask is waiting to be executed
        /// </summary>
        Pending,

        /// <summary>
        /// Subtask is currently being executed
        /// </summary>
        InProgress,

        /// <summary>
        /// Subtask has completed successfully
        /// </summary>
        Completed,

        /// <summary>
        /// Subtask execution failed with an error
        /// </summary>
        Failed,

        /// <summary>
        /// Subtask was skipped (optional step or user choice)
        /// </summary>
        Skipped,

        /// <summary>
        /// Subtask execution was cancelled
        /// </summary>
        Cancelled
    }
}
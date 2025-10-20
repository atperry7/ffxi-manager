using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents an item in the auto-login queue with task tracking and status management
    /// </summary>
    public class AutoLoginQueueItem : INotifyPropertyChanged
    {
        private int _position;
        private AutoLoginQueueStatus _status = AutoLoginQueueStatus.Pending;
        private string _statusMessage = string.Empty;
        private DateTime? _startTime;
        private DateTime? _endTime;
        private string _errorMessage = string.Empty;
        private AutoLoginTask? _task;

        /// <summary>
        /// Unique identifier for this queue item
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Associated PlayOnline Member Account
        /// </summary>
        public PlayOnlineMemberAccount Account
        {
            get => _account;
            set
            {
                if (_account != null)
                {
                    _account.PropertyChanged -= OnAccountPropertyChanged;
                }

                _account = value;

                if (_account != null)
                {
                    _account.PropertyChanged += OnAccountPropertyChanged;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
        private PlayOnlineMemberAccount _account = null!;

        /// <summary>
        /// Profile that contains this account (for cross-profile support)
        /// </summary>
        public ProfileInfo Profile
        {
            get => _profile;
            set
            {
                if (_profile != null)
                {
                    _profile.PropertyChanged -= OnProfilePropertyChanged;
                }

                _profile = value;

                if (_profile != null)
                {
                    _profile.PropertyChanged += OnProfilePropertyChanged;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(ProfileName));
            }
        }
        private ProfileInfo _profile = null!;

        /// <summary>
        /// Position in the queue (1-based)
        /// </summary>
        public int Position
        {
            get => _position;
            set => SetProperty(ref _position, value);
        }

        /// <summary>
        /// Current status of this queue item
        /// </summary>
        public AutoLoginQueueStatus Status
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
        /// Current status message
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Time when login process started
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
        /// Time when login process completed
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
        /// Error message if login failed
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
        /// The auto-login task associated with this queue item
        /// </summary>
        public AutoLoginTask? Task
        {
            get => _task;
            set
            {
                if (_task != null)
                {
                    _task.PropertyChanged -= OnTaskPropertyChanged;
                }

                _task = value;

                if (_task != null)
                {
                    _task.PropertyChanged += OnTaskPropertyChanged;

                    // Immediately synchronize timing with the task
                    _startTime = _task.StartTime;
                    _endTime = _task.EndTime;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(TaskProgress));
                OnPropertyChanged(nameof(CurrentTaskDisplay));
                OnPropertyChanged(nameof(CurrentSubtaskDisplay));
                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(DurationDisplay));
            }
        }

        #region Computed Properties

        /// <summary>
        /// Display name for the queue item
        /// </summary>
        public string DisplayName => _account?.DisplayName ?? "Unknown Account";

        /// <summary>
        /// Profile name for display
        /// </summary>
        public string ProfileName => _profile?.Name ?? "Unknown Profile";

        /// <summary>
        /// Whether this item is currently being processed
        /// </summary>
        public bool IsActive => Status == AutoLoginQueueStatus.InProgress;

        /// <summary>
        /// Whether this item has completed successfully
        /// </summary>
        public bool IsCompleted => Status == AutoLoginQueueStatus.Completed;

        /// <summary>
        /// Whether this item has an error
        /// </summary>
        public bool HasError => Status == AutoLoginQueueStatus.Failed || !string.IsNullOrEmpty(ErrorMessage);

        /// <summary>
        /// Status display text
        /// </summary>
        public string StatusDisplay => Status switch
        {
            AutoLoginQueueStatus.Pending => "Pending",
            AutoLoginQueueStatus.InProgress => "In Progress",
            AutoLoginQueueStatus.Paused => "Paused",
            AutoLoginQueueStatus.Completed => "Completed",
            AutoLoginQueueStatus.Failed => "Failed",
            AutoLoginQueueStatus.Cancelled => "Cancelled",
            _ => "Unknown"
        };

        /// <summary>
        /// Overall progress percentage (0-100) - Uses task-based progress tracking
        /// </summary>
        public int OverallProgress
        {
            get
            {
                if (Status == AutoLoginQueueStatus.Completed) return 100;
                if (Status == AutoLoginQueueStatus.Failed || Status == AutoLoginQueueStatus.Cancelled) return 0;

                // Use task progress (workflow-driven architecture)
                return Task?.Progress ?? 0;
            }
        }

        /// <summary>
        /// Task progress percentage (0-100) - New task-based progress
        /// </summary>
        public int TaskProgress => Task?.Progress ?? 0;

        /// <summary>
        /// Current task display text
        /// </summary>
        public string CurrentTaskDisplay => Task?.StatusMessage ?? "Waiting";

        /// <summary>
        /// Current subtask display text
        /// </summary>
        public string CurrentSubtaskDisplay => Task?.CurrentSubtask?.Name ?? "Waiting";

        /// <summary>
        /// Duration of login process - synchronized with task timing
        /// </summary>
        public TimeSpan? Duration
        {
            get
            {
                if (StartTime == null) return null;

                // Use EndTime if available (task completed), otherwise use current time only for active items
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

        #endregion

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

        /// <summary>
        /// Refreshes duration display for real-time updates
        /// </summary>
        public void RefreshDurationDisplay()
        {
            OnPropertyChanged(nameof(Duration));
            OnPropertyChanged(nameof(DurationDisplay));
        }

        /// <summary>
        /// Resets the queue item to pending state
        /// </summary>
        public void Reset()
        {
            Status = AutoLoginQueueStatus.Pending;
            StatusMessage = string.Empty;
            StartTime = null;
            EndTime = null;
            ErrorMessage = string.Empty;

            // Reset task if available
            Task?.Reset();
        }

        /// <summary>
        /// Handle property changes from the associated Account
        /// </summary>
        private void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayOnlineMemberAccount.DisplayName) ||
                e.PropertyName == nameof(PlayOnlineMemberAccount.AccountName) ||
                e.PropertyName == nameof(PlayOnlineMemberAccount.HasStoredPassword) ||
                e.PropertyName == nameof(PlayOnlineMemberAccount.IsOTPEnabled))
            {
                OnPropertyChanged(nameof(DisplayName));
            }

            // With canonical account instances and task rebuild per run, no special handling required here
        }

        /// <summary>
        /// Handle property changes from the associated Profile
        /// </summary>
        private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProfileInfo.Name))
            {
                OnPropertyChanged(nameof(ProfileName));
            }
        }

        /// <summary>
        /// Handle property changes from the associated Task
        /// </summary>
        private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AutoLoginTask.Progress) ||
                e.PropertyName == nameof(AutoLoginTask.Status) ||
                e.PropertyName == nameof(AutoLoginTask.CurrentSubtask) ||
                e.PropertyName == nameof(AutoLoginTask.StatusMessage))
            {
                OnPropertyChanged(nameof(TaskProgress));
                OnPropertyChanged(nameof(OverallProgress));
                OnPropertyChanged(nameof(CurrentTaskDisplay));
                OnPropertyChanged(nameof(CurrentSubtaskDisplay));
            }

            // Handle duration and timing property changes
            if (e.PropertyName == nameof(AutoLoginTask.Duration) ||
                e.PropertyName == nameof(AutoLoginTask.StartTime) ||
                e.PropertyName == nameof(AutoLoginTask.EndTime))
            {
                // Synchronize timing with the task to ensure they share the same timing
                if (Task != null)
                {
                    _startTime = Task.StartTime;
                    _endTime = Task.EndTime;
                }

                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(DurationDisplay));
            }
        }

        /// <summary>
        /// Clean up event subscriptions
        /// </summary>
        public void Cleanup()
        {
            if (_account != null)
            {
                _account.PropertyChanged -= OnAccountPropertyChanged;
            }
            if (_profile != null)
            {
                _profile.PropertyChanged -= OnProfilePropertyChanged;
            }
            if (_task != null)
            {
                _task.PropertyChanged -= OnTaskPropertyChanged;
                _task.Cleanup();
            }
        }
    }
}

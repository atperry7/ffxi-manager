using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents a PlayOnline character instance that is currently running
    /// </summary>
    public class PlayOnlineCharacter : INotifyPropertyChanged
    {
        private string _characterName = string.Empty;
        private string _serverName = string.Empty;
        private int _processId;
        private IntPtr _windowHandle;
        private string _windowTitle = string.Empty;
        private DateTime? _lastActivated;
        private DateTime _lastSeen = DateTime.UtcNow;
        private string _processName = string.Empty;
        private bool _isLastActivated;

        public string CharacterName
        {
            get => _characterName;
            set => SetProperty(ref _characterName, value);
        }

        public string ServerName
        {
            get => _serverName;
            set => SetProperty(ref _serverName, value);
        }

        public int ProcessId
        {
            get => _processId;
            set => SetProperty(ref _processId, value);
        }

        public IntPtr WindowHandle
        {
            get => _windowHandle;
            set => SetProperty(ref _windowHandle, value);
        }

        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        public DateTime? LastActivated
        {
            get => _lastActivated;
            set => SetProperty(ref _lastActivated, value);
        }

        public DateTime LastSeen
        {
            get => _lastSeen;
            set => SetProperty(ref _lastSeen, value);
        }

        public string ProcessName
        {
            get => _processName;
            set => SetProperty(ref _processName, value);
        }

        /// <summary>
        /// Display name for the character (shows character name if available, otherwise window title)
        /// </summary>
        public string DisplayName => !string.IsNullOrEmpty(CharacterName) ?
            (!string.IsNullOrEmpty(ServerName) ? $"{CharacterName} ({ServerName})" : CharacterName) :
            (!string.IsNullOrEmpty(WindowTitle) ? WindowTitle : $"FFXI Process {ProcessId}");

        /// <summary>
        /// Indicates if this character is running (process exists)
        /// </summary>
        public bool IsRunning => ProcessId > 0;

        /// <summary>
        /// Indicates if this character was recently activated (within last 10 seconds)
        /// </summary>
        public bool IsRecentlyActivated => LastActivated.HasValue && 
            (DateTime.UtcNow - LastActivated.Value).TotalSeconds < 10;

        /// <summary>
        /// Indicates if this character was the last one activated (persistent until another character is activated)
        /// </summary>
        public bool IsLastActivated
        {
            get => _isLastActivated;
            set => SetProperty(ref _isLastActivated, value);
        }

        /// <summary>
        /// Status text for display showing activation state
        /// </summary>
        public string StatusText
        {
            get
            {
                if (!LastActivated.HasValue) return "Running";
                
                var timeSinceActivation = DateTime.UtcNow - LastActivated.Value;
                if (timeSinceActivation.TotalSeconds < 10)
                    return "Recently Active";
                if (timeSinceActivation.TotalMinutes < 5)
                    return $"Last Active {(int)timeSinceActivation.TotalMinutes}m ago";
                    
                return "Running";
            }
        }

        /// <summary>
        /// Status color for UI display (string format for XAML compatibility)
        /// </summary>
        public string StatusColor => IsRecentlyActivated ? "Gold" : "Green";

        /// <summary>
        /// Status color as WPF Brush for direct binding (shows process health/running status)
        /// </summary>
        public Brush StatusBrush => IsRunning ? Brushes.LimeGreen : Brushes.Red;

        /// <summary>
        /// Marks this character as recently activated (called when switching to this character)
        /// </summary>
        public void MarkAsActivated()
        {
            LastActivated = DateTime.UtcNow;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            // Update dependent properties
            if (propertyName == nameof(CharacterName) || propertyName == nameof(ServerName) ||
                propertyName == nameof(WindowTitle) || propertyName == nameof(ProcessId))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
            else if (propertyName == nameof(LastActivated))
            {
                OnPropertyChanged(nameof(IsRecentlyActivated));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
            }
            else if (propertyName == nameof(ProcessId))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}

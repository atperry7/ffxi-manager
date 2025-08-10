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
        private bool _isActive;
        private DateTime _lastSeen = DateTime.UtcNow;
        private string _processName = string.Empty;

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

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
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
        /// Indicates if this character instance is currently responding
        /// </summary>
        public bool IsResponding => DateTime.UtcNow - LastSeen < TimeSpan.FromMinutes(1);

        /// <summary>
        /// Indicates if this character is running (process exists)
        /// </summary>
        public bool IsRunning => ProcessId > 0;

        /// <summary>
        /// Status text for display
        /// </summary>
        public string StatusText => IsActive ? "Active" : (IsResponding ? "Running" : "Not Responding");

        /// <summary>
        /// Status color for UI display (string format for XAML compatibility)
        /// </summary>
        public string StatusColor => IsActive ? "LightBlue" : (IsResponding ? "Green" : "Red");

        /// <summary>
        /// Status color as WPF Brush for direct binding
        /// </summary>
        public Brush StatusBrush
        {
            get
            {
                if (IsActive) return Brushes.LightBlue;
                if (IsResponding) return Brushes.LimeGreen;
                return Brushes.OrangeRed;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            
            // Update dependent properties
            if (propertyName == nameof(CharacterName) || propertyName == nameof(ServerName) || 
                propertyName == nameof(WindowTitle) || propertyName == nameof(ProcessId))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
            else if (propertyName == nameof(IsActive) || propertyName == nameof(LastSeen))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(IsResponding));
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
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents an external application that can be launched and monitored
    /// </summary>
    public class ExternalApplication : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _executablePath = string.Empty;
        private string _arguments = string.Empty;
        private string _workingDirectory = string.Empty;
        private bool _isRunning;
        private DateTime? _lastLaunched;
        private int _processId;
        private bool _isEnabled = true;
        private string _description = string.Empty;
        private bool _allowMultipleInstances;
        private int _currentInstances; // Add this for debugging

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string ExecutablePath
        {
            get => _executablePath;
            set => SetProperty(ref _executablePath, value);
        }

        public string Arguments
        {
            get => _arguments;
            set => SetProperty(ref _arguments, value);
        }

        public string WorkingDirectory
        {
            get => _workingDirectory;
            set => SetProperty(ref _workingDirectory, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        public DateTime? LastLaunched
        {
            get => _lastLaunched;
            set => SetProperty(ref _lastLaunched, value);
        }

        public int ProcessId
        {
            get => _processId;
            set => SetProperty(ref _processId, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public bool AllowMultipleInstances
        {
            get => _allowMultipleInstances;
            set => SetProperty(ref _allowMultipleInstances, value);
        }

        public int CurrentInstances
        {
            get => _currentInstances;
            set => SetProperty(ref _currentInstances, value);
        }

        /// <summary>
        /// Gets whether the application executable exists
        /// </summary>
        public bool ExecutableExists => File.Exists(ExecutablePath);

        /// <summary>
        /// Gets the status color for UI display
        /// </summary>
        public string StatusColor => IsRunning ? "Green" : (ExecutableExists ? "Gray" : "Red");

        /// <summary>
        /// Gets the status text for display
        /// </summary>
        public string StatusText => IsRunning ? "Running" : (ExecutableExists ? "Stopped" : "Not Found");

        public event PropertyChangedEventHandler? PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            
            // Update dependent properties when key properties change
            if (propertyName == nameof(IsRunning) || propertyName == nameof(ExecutablePath))
            {
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ExecutableExists));
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
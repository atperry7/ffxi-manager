using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
        private bool _isEnabled = true;
        private string _description = string.Empty;
        private bool _allowMultipleInstances;
        private int _currentInstances;
        private readonly List<int> _processIds = new();
        
        // Caching for ExecutableExists to reduce file system calls
        private bool? _cachedExecutableExists;
        private string _cachedExecutablePath = string.Empty;
        private DateTime _lastFileCheckTime = DateTime.MinValue;
        private static readonly TimeSpan FileCheckCacheTime = TimeSpan.FromSeconds(5); // Cache for 5 seconds

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string ExecutablePath
        {
            get => _executablePath;
            set 
            { 
                if (SetProperty(ref _executablePath, value))
                {
                    // Clear cache when path changes
                    _cachedExecutableExists = null;
                    _cachedExecutablePath = string.Empty;
                    OnPropertyChanged(nameof(ExecutableExists));
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
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
        /// List of tracked Process IDs for this application
        /// </summary>
        public IReadOnlyList<int> ProcessIds => _processIds;

        /// <summary>
        /// Replace the entire PID list (keeps notifications consistent)
        /// </summary>
        public void SetProcessIds(IEnumerable<int> pids)
        {
            var newList = pids.Distinct().Where(pid => pid > 0).ToList();
            bool changed = !_processIds.SequenceEqual(newList);
            if (changed)
            {
                _processIds.Clear();
                _processIds.AddRange(newList);
                CurrentInstances = _processIds.Count;
                IsRunning = _processIds.Count > 0;
                OnPropertyChanged(nameof(ProcessIds));
            }
        }

        /// <summary>
        /// Add a PID if not already tracked
        /// </summary>
        public bool AddProcessId(int pid)
        {
            if (pid <= 0) return false;
            if (_processIds.Contains(pid)) return false;
            _processIds.Add(pid);
            CurrentInstances = _processIds.Count;
            IsRunning = true;
            OnPropertyChanged(nameof(ProcessIds));
            return true;
        }

        /// <summary>
        /// Remove a PID if tracked
        /// </summary>
        public bool RemoveProcessId(int pid)
        {
            var removed = _processIds.Remove(pid);
            if (removed)
            {
                CurrentInstances = _processIds.Count;
                IsRunning = _processIds.Count > 0;
                OnPropertyChanged(nameof(ProcessIds));
            }
            return removed;
        }

        /// <summary>
        /// Gets whether the application executable exists (with caching to reduce file system calls)
        /// </summary>
        public bool ExecutableExists
        {
            get
            {
                // Skip check if path is empty or invalid
                if (string.IsNullOrWhiteSpace(ExecutablePath))
                    return false;

                var now = DateTime.UtcNow;
                
                // Use cached value if:
                // 1. We have a cached value
                // 2. The path hasn't changed
                // 3. The cache is still valid (within 5 seconds)
                if (_cachedExecutableExists.HasValue && 
                    _cachedExecutablePath == ExecutablePath && 
                    now - _lastFileCheckTime < FileCheckCacheTime)
                {
                    return _cachedExecutableExists.Value;
                }

                // Perform file check with error handling
                bool exists = false;
                try
                {
                    exists = File.Exists(ExecutablePath);
                }
                catch (Exception)
                {
                    // If file check fails (IOException, etc.), assume false
                    exists = false;
                }

                // Update cache
                _cachedExecutableExists = exists;
                _cachedExecutablePath = ExecutablePath;
                _lastFileCheckTime = now;

                return exists;
            }
        }

        /// <summary>
        /// Forces a refresh of the ExecutableExists cache
        /// </summary>
        public void RefreshExecutableExists()
        {
            _cachedExecutableExists = null;
            _cachedExecutablePath = string.Empty;
            OnPropertyChanged(nameof(ExecutableExists));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(StatusText));
        }

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
                if (propertyName == nameof(ExecutablePath))
                {
                    OnPropertyChanged(nameof(ExecutableExists));
                }
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

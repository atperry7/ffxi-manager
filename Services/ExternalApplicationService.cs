using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Infrastructure;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for managing external applications
    /// </summary>
    public interface IExternalApplicationService
    {
        Task<List<ExternalApplication>> GetApplicationsAsync();
        Task<ExternalApplication> AddApplicationAsync(ExternalApplication application);
        Task UpdateApplicationAsync(ExternalApplication application);
        Task RemoveApplicationAsync(ExternalApplication application);
        Task<bool> LaunchApplicationAsync(ExternalApplication application);
        Task<bool> KillApplicationAsync(ExternalApplication application);
        Task RefreshApplicationStatusAsync();
        Task RefreshApplicationStatusAsync(ExternalApplication application);
        void StartMonitoring();
        void StopMonitoring();
        event EventHandler<ExternalApplication>? ApplicationStatusChanged;
    }

    /// <summary>
    /// Service for managing and monitoring external applications
    /// Uses shared ProcessManagementService for unified process handling
    /// </summary>
    public class ExternalApplicationService : IExternalApplicationService, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly ISettingsService _settingsService;
        private readonly IProcessManagementService _processManagementService;
        private readonly List<ExternalApplication> _applications = new();
        private readonly Dictionary<int, ExternalApplication> _processToAppMap = new();
        private readonly object _lockObject = new();
        private readonly HashSet<string> _registeredProcessNames = new(StringComparer.OrdinalIgnoreCase);
        private bool _isMonitoring;
        private bool _disposed;

        public event EventHandler<ExternalApplication>? ApplicationStatusChanged;

        public ExternalApplicationService(ILoggingService loggingService, ISettingsService settingsService, IProcessManagementService processManagementService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _processManagementService = processManagementService ?? throw new ArgumentNullException(nameof(processManagementService));
            
            // Subscribe to global process events
            _processManagementService.ProcessDetected += OnProcessDetected;
            _processManagementService.ProcessTerminated += OnProcessTerminated;
            _processManagementService.ProcessUpdated += OnProcessUpdated;
            
            LoadApplicationsFromSettings();
        }

        public async Task<List<ExternalApplication>> GetApplicationsAsync()
        {
            await _loggingService.LogDebugAsync("Getting applications list", "ExternalApplicationService");
            
            // Immediately refresh status when applications are requested
            await RefreshApplicationStatusAsync();
            
            lock (_lockObject)
            {
                return new List<ExternalApplication>(_applications);
            }
        }

        public async Task<ExternalApplication> AddApplicationAsync(ExternalApplication application)
        {
            if (application == null)
                throw new ArgumentNullException(nameof(application));

            await _loggingService.LogInfoAsync($"Adding application: {application.Name}", "ExternalApplicationService");
            
            lock (_lockObject)
            {
                _applications.Add(application);
            }
            await SaveApplicationsToSettings();

            RefreshRegisteredProcessNames();

            return application;
        }

        public async Task UpdateApplicationAsync(ExternalApplication application)
        {
            if (application == null)
                throw new ArgumentNullException(nameof(application));

            await _loggingService.LogInfoAsync($"Updating application: {application.Name}", "ExternalApplicationService");
            await SaveApplicationsToSettings();

            RefreshRegisteredProcessNames();
        }

        public async Task RemoveApplicationAsync(ExternalApplication application)
        {
            if (application == null)
                throw new ArgumentNullException(nameof(application));

            await _loggingService.LogInfoAsync($"Removing application: {application.Name}", "ExternalApplicationService");
            
            // Kill the application if it's running
            if (application.IsRunning)
            {
                await KillApplicationAsync(application);
            }
            
            lock (_lockObject)
            {
                _applications.Remove(application);
            }
            await SaveApplicationsToSettings();

            RefreshRegisteredProcessNames();
        }

        public async Task<bool> LaunchApplicationAsync(ExternalApplication application)
        {
            if (application == null)
                throw new ArgumentNullException(nameof(application));

            await _loggingService.LogInfoAsync($"Launching application: {application.Name}", "ExternalApplicationService");

            if (!application.ExecutableExists)
            {
                await _loggingService.LogWarningAsync($"Application executable not found: {application.ExecutablePath}", "ExternalApplicationService");
                return false;
            }

            if (!application.AllowMultipleInstances && application.IsRunning)
            {
                await _loggingService.LogWarningAsync($"Application {application.Name} is already running and multiple instances are not allowed", "ExternalApplicationService");
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = application.ExecutablePath,
                    Arguments = application.Arguments,
                    WorkingDirectory = string.IsNullOrEmpty(application.WorkingDirectory) 
                        ? Path.GetDirectoryName(application.ExecutablePath) ?? Environment.CurrentDirectory
                        : application.WorkingDirectory,
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                if (process != null)
                {
                    application.ProcessId = process.Id;
                    application.LastLaunched = DateTime.Now;
                    application.IsRunning = true;
                    
                    lock (_lockObject)
                    {
                        _processToAppMap[process.Id] = application;
                    }
                    
                    await _loggingService.LogInfoAsync($"Successfully launched {application.Name} (PID: {process.Id})", "ExternalApplicationService");
                    ApplicationStatusChanged?.Invoke(this, application);
                    
                    return true;
                }
                else
                {
                    await _loggingService.LogErrorAsync($"Failed to start process for {application.Name}", null, "ExternalApplicationService");
                    return false;
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error launching {application.Name}", ex, "ExternalApplicationService");
                return false;
            }
        }

        public async Task<bool> KillApplicationAsync(ExternalApplication application)
        {
            if (application == null || !application.IsRunning)
                return false;

            await _loggingService.LogInfoAsync($"Killing application: {application.Name}", "ExternalApplicationService");

            try
            {
                var oldProcessId = application.ProcessId;
                
                // Use shared process management service for killing processes
                bool success = await _processManagementService.KillProcessAsync(application.ProcessId, 5000);
                
                if (success)
                {
                    application.IsRunning = false;
                    application.ProcessId = 0;
                    
                    lock (_lockObject)
                    {
                        _processToAppMap.Remove(oldProcessId);
                    }
                    
                    await _loggingService.LogInfoAsync($"Successfully killed {application.Name}", "ExternalApplicationService");
                    ApplicationStatusChanged?.Invoke(this, application);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error killing {application.Name}", ex, "ExternalApplicationService");
                return false;
            }
        }

        public async Task RefreshApplicationStatusAsync()
        {
            await _loggingService.LogDebugAsync("Refreshing all application statuses", "ExternalApplicationService");
            
            var applications = new List<ExternalApplication>();
            lock (_lockObject)
            {
                applications.AddRange(_applications);
            }
            
            foreach (var app in applications)
            {
                await RefreshApplicationStatusAsync(app);
            }
        }

        public async Task RefreshApplicationStatusAsync(ExternalApplication application)
        {
            if (application == null) return;

            try
            {
                // Skip if executable path is empty or invalid
                if (string.IsNullOrWhiteSpace(application.ExecutablePath))
                {
                    application.IsRunning = false;
                    application.ProcessId = 0;
                    application.CurrentInstances = 0;
                    return;
                }

                var processName = Path.GetFileNameWithoutExtension(application.ExecutablePath);
                
                // Skip process check if we can't get a valid process name
                if (string.IsNullOrWhiteSpace(processName))
                {
                    application.IsRunning = false;
                    application.ProcessId = 0;
                    application.CurrentInstances = 0;
                    return;
                }

                var wasRunning = application.IsRunning;
                var oldProcessId = application.ProcessId;
                
                // Use shared process management service to get processes
                var processes = await _processManagementService.GetProcessesByNamesAsync(new[] { processName });
                
                // Check if our specific tracked process is still running
                bool isOurProcessRunning = false;
                if (application.ProcessId > 0)
                {
                    isOurProcessRunning = _processManagementService.IsProcessRunning(application.ProcessId);
                }

                // Update the running status based on our tracked process or any running instance
                bool hasRunningInstances = processes.Count > 0;
                
                if (application.ProcessId > 0 && !isOurProcessRunning)
                {
                    // Our specific process has stopped
                    application.IsRunning = false;
                    lock (_lockObject)
                    {
                        _processToAppMap.Remove(oldProcessId);
                    }
                    application.ProcessId = 0;
                }
                else if (hasRunningInstances && !application.IsRunning)
                {
                    // New instance detected
                    application.IsRunning = true;
                    application.ProcessId = processes[0].ProcessId;
                    lock (_lockObject)
                    {
                        _processToAppMap[application.ProcessId] = application;
                    }
                }
                else if (!hasRunningInstances && application.IsRunning)
                {
                    // All instances stopped
                    application.IsRunning = false;
                    if (oldProcessId > 0)
                    {
                        lock (_lockObject)
                        {
                            _processToAppMap.Remove(oldProcessId);
                        }
                    }
                    application.ProcessId = 0;
                }

                // Update current instances count
                application.CurrentInstances = processes.Count;

                // Only trigger property notifications if status actually changed
                if (wasRunning != application.IsRunning)
                {
                    application.OnPropertyChanged(nameof(application.IsRunning));
                    application.OnPropertyChanged(nameof(application.StatusColor));
                    application.OnPropertyChanged(nameof(application.StatusText));
                    
                    await _loggingService.LogInfoAsync($"Application {application.Name} status changed: {(application.IsRunning ? "STARTED" : "STOPPED")}", "ExternalApplicationService");
                    ApplicationStatusChanged?.Invoke(this, application);
                }
                else
                {
                    // Still notify CurrentInstances if it changed
                    application.OnPropertyChanged(nameof(application.CurrentInstances));
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogWarningAsync($"Error refreshing status for {application.Name}: {ex.Message}", "ExternalApplicationService");
            }
        }

        public void StartMonitoring()
        {
            if (!_isMonitoring)
            {
                _isMonitoring = true;
                
                // Start global monitoring if not already started
                _processManagementService.StartGlobalMonitoring(TimeSpan.FromSeconds(5));
                
                _loggingService.LogInfoAsync("Started application monitoring", "ExternalApplicationService");
            }
        }

        public void StopMonitoring()
        {
            if (_isMonitoring)
            {
                _isMonitoring = false;
                _loggingService.LogInfoAsync("Stopped application monitoring", "ExternalApplicationService");
            }
        }

        #region Process Event Handlers

        private void OnProcessDetected(object? sender, ProcessInfo processInfo)
        {
            if (!_isMonitoring) return;
            
            Task.Run(async () =>
            {
                try
                {
                    // Check if this process matches any of our applications
                    var processName = processInfo.ProcessName;
                    ExternalApplication? matchingApp = null;
                    
                    lock (_lockObject)
                    {
                        matchingApp = _applications.FirstOrDefault(app => 
                            !string.IsNullOrEmpty(app.ExecutablePath) &&
                            string.Equals(Path.GetFileNameWithoutExtension(app.ExecutablePath), 
                                         processName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (matchingApp != null)
                    {
                        await RefreshApplicationStatusAsync(matchingApp);
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogDebugAsync($"Error handling process detection: {ex.Message}", "ExternalApplicationService");
                }
            });
        }

        private void OnProcessTerminated(object? sender, ProcessInfo processInfo)
        {
            ExternalApplication? app = null;
            
            lock (_lockObject)
            {
                _processToAppMap.TryGetValue(processInfo.ProcessId, out app);
                if (app != null)
                {
                    _processToAppMap.Remove(processInfo.ProcessId);
                }
            }

            if (app != null)
            {
                app.IsRunning = false;
                app.ProcessId = 0;
                ApplicationStatusChanged?.Invoke(this, app);
            }
        }

        private void OnProcessUpdated(object? sender, ProcessInfo processInfo)
        {
            if (!_isMonitoring) return;

            ExternalApplication? app = null;
            lock (_lockObject)
            {
                _processToAppMap.TryGetValue(processInfo.ProcessId, out app);
            }

            if (app != null)
            {
                // Update application information based on process changes
                // This could include responsiveness checks, etc.
                app.OnPropertyChanged(nameof(app.StatusText));
            }
        }

        #endregion

        #region Settings Management

        private void LoadApplicationsFromSettings()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                
                // If we have saved applications, load them directly
                if (settings.ExternalApplications != null && settings.ExternalApplications.Count > 0)
                {
                    // Load all applications from settings (including previously saved defaults)
                    foreach (var appData in settings.ExternalApplications)
                    {
                        var application = new ExternalApplication
                        {
                            Name = appData.Name,
                            ExecutablePath = appData.ExecutablePath,
                            Arguments = appData.Arguments,
                            WorkingDirectory = appData.WorkingDirectory,
                            Description = appData.Description,
                            IsEnabled = appData.IsEnabled,
                            AllowMultipleInstances = appData.AllowMultipleInstances
                        };
                        _applications.Add(application);
                    }
                    
                    _loggingService.LogInfoAsync($"Loaded {_applications.Count} applications from settings", "ExternalApplicationService");
                }
                else
                {
                    // First run - load default applications and save them immediately
                    _applications.AddRange(GetDefaultApplications());
                    _loggingService.LogInfoAsync($"First run - loaded {_applications.Count} default applications", "ExternalApplicationService");

                    // Save defaults to settings so they persist
                    _ = Task.Run(async () => await SaveApplicationsToSettings());
                }

                RefreshRegisteredProcessNames();
            }
            catch (Exception ex)
            {
                // If loading fails, fall back to default applications only
                _applications.Clear();
                _applications.AddRange(GetDefaultApplications());
                _loggingService.LogErrorAsync($"Failed to load applications from settings, using defaults: {ex.Message}", ex, "ExternalApplicationService");

                // Try to save defaults
                _ = Task.Run(async () => await SaveApplicationsToSettings());

                RefreshRegisteredProcessNames();
            }
        }

        private async Task SaveApplicationsToSettings()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                
                List<ExternalApplication> applicationsToSave;
                lock (_lockObject)
                {
                    applicationsToSave = new List<ExternalApplication>(_applications);
                }
                
                // Convert applications to serializable format
                settings.ExternalApplications = applicationsToSave.Select(app => new ExternalApplicationData
                {
                    Name = app.Name,
                    ExecutablePath = app.ExecutablePath,
                    Arguments = app.Arguments,
                    WorkingDirectory = app.WorkingDirectory,
                    Description = app.Description,
                    IsEnabled = app.IsEnabled,
                    AllowMultipleInstances = app.AllowMultipleInstances
                }).ToList();
                
                _settingsService.SaveSettings(settings);
                
                await _loggingService.LogInfoAsync($"Successfully saved {applicationsToSave.Count} applications to settings", "ExternalApplicationService");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Failed to save applications to settings", ex, "ExternalApplicationService");
            }
        }

        private string? GetProcessName(ExternalApplication application)
        {
            if (application == null || string.IsNullOrWhiteSpace(application.ExecutablePath))
                return null;

            try
            {
                return Path.GetFileNameWithoutExtension(application.ExecutablePath);
            }
            catch
            {
                return null;
            }
        }

        private void RefreshRegisteredProcessNames()
        {
            var desiredNames = _applications
                .Select(GetProcessName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var toAdd = desiredNames.Except(_registeredProcessNames, StringComparer.OrdinalIgnoreCase).ToList();
            var toRemove = _registeredProcessNames.Except(desiredNames, StringComparer.OrdinalIgnoreCase).ToList();

            if (toAdd.Count > 0)
                _processManagementService.AddProcessNames(toAdd);
            if (toRemove.Count > 0)
                _processManagementService.RemoveProcessNames(toRemove);

            _registeredProcessNames.Clear();
            foreach (var name in desiredNames)
            {
                _registeredProcessNames.Add(name);
            }
        }

        private List<ExternalApplication> GetDefaultApplications()
        {
            var applications = new List<ExternalApplication>();

            // Try to find common installation paths for applications
            var potentialPaths = new Dictionary<string, string[]>
            {
                ["POL Proxy"] = new[]
                {
                    @"C:\Program Files\POLProxy\POLProxy.exe",
                    @"C:\Program Files (x86)\POLProxy\POLProxy.exe",
                    @"C:\POLProxy\POLProxy.exe",
                    @"D:\POLProxy\POLProxy.exe"
                },
                ["Windower"] = new[]
                {
                    @"C:\Windower4\Windower.exe",
                    @"C:\Program Files\Windower4\Windower.exe",
                    @"C:\Program Files (x86)\Windower4\Windower.exe",
                    @"D:\Windower4\Windower.exe"
                },
                ["Silmaril"] = new[]
                {
                    @"C:\Program Files\Silmaril\Silmaril.exe",
                    @"C:\Program Files (x86)\Silmaril\Silmaril.exe",
                    @"C:\Silmaril\Silmaril.exe",
                    @"D:\Silmaril\Silmaril.exe"
                }
            };

            foreach (var app in potentialPaths)
            {
                var foundPath = string.Empty;
                
                // Try to find an existing path
                foreach (var path in app.Value)
                {
                    if (File.Exists(path))
                    {
                        foundPath = path;
                        break;
                    }
                }

                // Use first path as default even if not found (user can edit it)
                if (string.IsNullOrEmpty(foundPath))
                {
                    foundPath = app.Value[0];
                }

                var application = new ExternalApplication
                {
                    Name = app.Key,
                    ExecutablePath = foundPath,
                    IsEnabled = true
                };

                switch (app.Key)
                {
                    case "POL Proxy":
                        application.Description = "PlayOnline Proxy Server";
                        application.AllowMultipleInstances = false;
                        break;
                    case "Windower":
                        application.Description = "FFXI Game Launcher";
                        application.AllowMultipleInstances = true; // Allow multiple characters
                        break;
                    case "Silmaril":
                        application.Description = "FFXI Utility Tool";
                        application.AllowMultipleInstances = false;
                        break;
                }

                applications.Add(application);
            }

            return applications;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopMonitoring();

            if (_registeredProcessNames.Count > 0)
            {
                _processManagementService.RemoveProcessNames(_registeredProcessNames);
                _registeredProcessNames.Clear();
            }

            // Unsubscribe from process events
            _processManagementService.ProcessDetected -= OnProcessDetected;
            _processManagementService.ProcessTerminated -= OnProcessTerminated;
            _processManagementService.ProcessUpdated -= OnProcessUpdated;
        }
    }
}
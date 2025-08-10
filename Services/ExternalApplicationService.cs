using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFXIManager.Models;

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
    /// </summary>
    public class ExternalApplicationService : IExternalApplicationService, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly ISettingsService _settingsService;
        private readonly List<ExternalApplication> _applications = new();
        private readonly System.Threading.Timer? _monitoringTimer;
        private readonly Dictionary<int, ExternalApplication> _processToAppMap = new();
        private bool _isMonitoring;

        public event EventHandler<ExternalApplication>? ApplicationStatusChanged;

        public ExternalApplicationService(ILoggingService loggingService, ISettingsService settingsService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            
            // Monitor processes every 3 seconds
            _monitoringTimer = new System.Threading.Timer(MonitorProcesses, null, 
                TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
            
            LoadApplicationsFromSettings();
        }

        public async Task<List<ExternalApplication>> GetApplicationsAsync()
        {
            await _loggingService.LogDebugAsync("Getting applications list", "ExternalApplicationService");
            
            // Immediately refresh status when applications are requested
            await RefreshApplicationStatusAsync();
            
            return new List<ExternalApplication>(_applications);
        }

        public async Task<ExternalApplication> AddApplicationAsync(ExternalApplication application)
        {
            if (application == null)
                throw new ArgumentNullException(nameof(application));

            await _loggingService.LogInfoAsync($"Adding application: {application.Name}", "ExternalApplicationService");
            
            _applications.Add(application);
            await SaveApplicationsToSettings();
            
            return application;
        }

        public async Task UpdateApplicationAsync(ExternalApplication application)
        {
            if (application == null)
                throw new ArgumentNullException(nameof(application));

            await _loggingService.LogInfoAsync($"Updating application: {application.Name}", "ExternalApplicationService");
            await SaveApplicationsToSettings();
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
            
            _applications.Remove(application);
            await SaveApplicationsToSettings();
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
                    
                    _processToAppMap[process.Id] = application;
                    
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
                var process = Process.GetProcessById(application.ProcessId);
                process.Kill();
                process.WaitForExit(5000); // Wait up to 5 seconds
                
                application.IsRunning = false;
                application.ProcessId = 0;
                _processToAppMap.Remove(oldProcessId); // Use the old ProcessId, not the current (which is now 0)
                
                await _loggingService.LogInfoAsync($"Successfully killed {application.Name}", "ExternalApplicationService");
                ApplicationStatusChanged?.Invoke(this, application);
                
                return true;
            }
            catch (ArgumentException)
            {
                // Process already exited
                var oldProcessId = application.ProcessId;
                application.IsRunning = false;
                application.ProcessId = 0;
                if (oldProcessId > 0)
                    _processToAppMap.Remove(oldProcessId);
                ApplicationStatusChanged?.Invoke(this, application);
                return true;
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
            
            foreach (var app in _applications)
            {
                await RefreshApplicationStatusAsync(app);
            }
        }

        public async Task RefreshApplicationStatusAsync(ExternalApplication application)
        {
            if (application == null) return;

            try
            {
                var processName = Path.GetFileNameWithoutExtension(application.ExecutablePath);
                await _loggingService.LogDebugAsync($"Checking for process: '{processName}' (from path: {application.ExecutablePath})", "ExternalApplicationService");
                
                var runningProcesses = Process.GetProcessesByName(processName);
                await _loggingService.LogDebugAsync($"Found {runningProcesses.Length} instances of '{processName}'", "ExternalApplicationService");
                
                var wasRunning = application.IsRunning;
                var oldProcessId = application.ProcessId;
                
                // Check if our specific tracked process is still running
                bool isOurProcessRunning = false;
                if (application.ProcessId > 0)
                {
                    try
                    {
                        var trackedProcess = Process.GetProcessById(application.ProcessId);
                        isOurProcessRunning = !trackedProcess.HasExited;
                        await _loggingService.LogDebugAsync($"Our tracked process {application.ProcessId} is {(isOurProcessRunning ? "running" : "stopped")}", "ExternalApplicationService");
                    }
                    catch (ArgumentException)
                    {
                        // Process no longer exists
                        isOurProcessRunning = false;
                        await _loggingService.LogDebugAsync($"Our tracked process {application.ProcessId} no longer exists", "ExternalApplicationService");
                    }
                }

                // Update the running status based on our tracked process or any running instance
                bool hasRunningInstances = runningProcesses.Length > 0;
                
                if (application.ProcessId > 0 && !isOurProcessRunning)
                {
                    // Our specific process has stopped
                    application.IsRunning = false;
                    _processToAppMap.Remove(oldProcessId);
                    application.ProcessId = 0;
                    await _loggingService.LogDebugAsync($"Application {application.Name} detected as STOPPED (our process ended)", "ExternalApplicationService");
                }
                else if (hasRunningInstances && !application.IsRunning)
                {
                    // New instance detected
                    application.IsRunning = true;
                    application.ProcessId = runningProcesses[0].Id;
                    _processToAppMap[application.ProcessId] = application;
                    await _loggingService.LogDebugAsync($"Application {application.Name} detected as RUNNING (PID: {application.ProcessId})", "ExternalApplicationService");
                }
                else if (!hasRunningInstances && application.IsRunning)
                {
                    // All instances stopped
                    application.IsRunning = false;
                    if (oldProcessId > 0)
                        _processToAppMap.Remove(oldProcessId);
                    application.ProcessId = 0;
                    await _loggingService.LogDebugAsync($"Application {application.Name} detected as STOPPED (no instances found)", "ExternalApplicationService");
                }

                // Update current instances count
                application.CurrentInstances = runningProcesses.Length;

                // Force property change notifications to update UI
                application.OnPropertyChanged(nameof(application.IsRunning));
                application.OnPropertyChanged(nameof(application.StatusColor));
                application.OnPropertyChanged(nameof(application.StatusText));
                application.OnPropertyChanged(nameof(application.ExecutableExists));
                application.OnPropertyChanged(nameof(application.CurrentInstances));

                if (wasRunning != application.IsRunning)
                {
                    await _loggingService.LogInfoAsync($"Application {application.Name} status changed: {(application.IsRunning ? "STARTED" : "STOPPED")}", "ExternalApplicationService");
                    ApplicationStatusChanged?.Invoke(this, application);
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogWarningAsync($"Error refreshing status for {application.Name}: {ex.Message}", "ExternalApplicationService");
            }
        }

        public void StartMonitoring()
        {
            _isMonitoring = true;
            _loggingService.LogInfoAsync("Started application monitoring", "ExternalApplicationService");
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
            _loggingService.LogInfoAsync("Stopped application monitoring", "ExternalApplicationService");
        }

        private async void MonitorProcesses(object? state)
        {
            if (!_isMonitoring) return;

            try
            {
                await RefreshApplicationStatusAsync();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error during process monitoring", ex, "ExternalApplicationService");
            }
        }

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
            }
            catch (Exception ex)
            {
                // If loading fails, fall back to default applications only
                _applications.Clear();
                _applications.AddRange(GetDefaultApplications());
                _loggingService.LogErrorAsync($"Failed to load applications from settings, using defaults: {ex.Message}", ex, "ExternalApplicationService");
                
                // Try to save defaults
                _ = Task.Run(async () => await SaveApplicationsToSettings());
            }
        }

        private async Task SaveApplicationsToSettings()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                
                // Convert applications to serializable format
                settings.ExternalApplications = _applications.Select(app => new ExternalApplicationData
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
                
                await _loggingService.LogInfoAsync($"Successfully saved {_applications.Count} applications to settings", "ExternalApplicationService");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Failed to save applications to settings", ex, "ExternalApplicationService");
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

        public void Dispose()
        {
            _monitoringTimer?.Dispose();
            StopMonitoring();
        }
    }
}
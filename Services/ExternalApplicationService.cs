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
                var process = Process.GetProcessById(application.ProcessId);
                process.Kill();
                process.WaitForExit(5000); // Wait up to 5 seconds
                
                application.IsRunning = false;
                application.ProcessId = 0;
                _processToAppMap.Remove(application.ProcessId);
                
                await _loggingService.LogInfoAsync($"Successfully killed {application.Name}", "ExternalApplicationService");
                ApplicationStatusChanged?.Invoke(this, application);
                
                return true;
            }
            catch (ArgumentException)
            {
                // Process already exited
                application.IsRunning = false;
                application.ProcessId = 0;
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
                await _loggingService.LogDebugAsync($"?? Checking for process: '{processName}' (from path: {application.ExecutablePath})", "ExternalApplicationService");
                
                var runningProcesses = Process.GetProcessesByName(processName);
                await _loggingService.LogDebugAsync($"?? Found {runningProcesses.Length} instances of '{processName}'", "ExternalApplicationService");
                
                var wasRunning = application.IsRunning;
                var previousInstances = application.CurrentInstances;
                
                // Update the running status
                application.IsRunning = runningProcesses.Length > 0;

                if (application.IsRunning && runningProcesses.Length > 0)
                {
                    application.ProcessId = runningProcesses[0].Id;
                    _processToAppMap[application.ProcessId] = application;
                    await _loggingService.LogDebugAsync($"? Application {application.Name} detected as RUNNING (PID: {application.ProcessId})", "ExternalApplicationService");
                }
                else if (!application.IsRunning)
                {
                    if (application.ProcessId > 0)
                        _processToAppMap.Remove(application.ProcessId);
                    application.ProcessId = 0;
                    await _loggingService.LogDebugAsync($"? Application {application.Name} detected as STOPPED", "ExternalApplicationService");
                }

                // Force property change notifications to update UI
                application.OnPropertyChanged(nameof(application.IsRunning));
                application.OnPropertyChanged(nameof(application.StatusColor));
                application.OnPropertyChanged(nameof(application.StatusText));
                application.OnPropertyChanged(nameof(application.ExecutableExists));

                if (wasRunning != application.IsRunning)
                {
                    await _loggingService.LogInfoAsync($"?? Application {application.Name} status changed: {(application.IsRunning ? "STARTED" : "STOPPED")}", "ExternalApplicationService");
                    ApplicationStatusChanged?.Invoke(this, application);
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogWarningAsync($"? Error refreshing status for {application.Name}: {ex.Message}", "ExternalApplicationService");
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
            // For now, add some default applications
            _applications.AddRange(GetDefaultApplications());
        }

        private async Task SaveApplicationsToSettings()
        {
            // TODO: Implement settings persistence
            await _loggingService.LogDebugAsync("Applications saved to settings", "ExternalApplicationService");
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
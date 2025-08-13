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
    /// Simplified external application service using UnifiedMonitoringService
    /// </summary>
    public class ExternalApplicationService : IExternalApplicationService, IDisposable
    {
        private readonly IUnifiedMonitoringService _unifiedMonitoring;
        private readonly ILoggingService _logging;
        private readonly ISettingsService _settings;
        
        private readonly List<ExternalApplication> _applications = new();
        private readonly Dictionary<Guid, ExternalApplication> _monitorToAppMap = new();
        private readonly object _lock = new();
        
        private bool _isMonitoring;
        private bool _disposed;
        
        public event EventHandler<ExternalApplication>? ApplicationStatusChanged;
        
        public ExternalApplicationService(
            IUnifiedMonitoringService unifiedMonitoring,
            ILoggingService logging,
            ISettingsService settings)
        {
            _unifiedMonitoring = unifiedMonitoring ?? throw new ArgumentNullException(nameof(unifiedMonitoring));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            
            // Load applications from settings
            LoadApplicationsFromSettings();
            
            // Register monitoring profiles for each application
            RegisterApplicationProfiles();
            
            // Subscribe to unified monitoring events
            _unifiedMonitoring.ProcessDetected += OnProcessDetected;
            _unifiedMonitoring.ProcessUpdated += OnProcessUpdated;
            _unifiedMonitoring.ProcessRemoved += OnProcessRemoved;
        }
        
        public async Task<List<ExternalApplication>> GetApplicationsAsync()
        {
            await Task.Yield(); // Ensure async context
            
            // Refresh status from unified monitoring
            await RefreshApplicationStatusAsync();
            
            lock (_lock)
            {
                return new List<ExternalApplication>(_applications);
            }
        }
        
        public async Task<ExternalApplication> AddApplicationAsync(ExternalApplication application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            
            await _logging.LogInfoAsync($"Adding application: {application.Name}", "ExternalApplicationService");
            
            lock (_lock)
            {
                _applications.Add(application);
            }
            
            // Register monitoring profile for this application
            RegisterApplicationProfile(application);
            
            // Save to settings
            await SaveApplicationsToSettings();
            
            // Check if it's already running
            await RefreshApplicationStatusAsync(application);
            
            return application;
        }
        
        public async Task UpdateApplicationAsync(ExternalApplication application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            
            await _logging.LogInfoAsync($"Updating application: {application.Name}", "ExternalApplicationService");
            
            // Update monitoring profile
            UpdateApplicationProfile(application);
            
            // Save to settings
            await SaveApplicationsToSettings();
            
            // Refresh status
            await RefreshApplicationStatusAsync(application);
        }
        
        public async Task RemoveApplicationAsync(ExternalApplication application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            
            await _logging.LogInfoAsync($"Removing application: {application.Name}", "ExternalApplicationService");
            
            // Kill if running
            if (application.IsRunning)
            {
                await KillApplicationAsync(application);
            }
            
            // Unregister monitoring profile
            UnregisterApplicationProfile(application);
            
            lock (_lock)
            {
                _applications.Remove(application);
            }
            
            // Save to settings
            await SaveApplicationsToSettings();
        }
        
        public async Task<bool> LaunchApplicationAsync(ExternalApplication application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            
            await _logging.LogInfoAsync($"Launching application: {application.Name}", "ExternalApplicationService");
            
            if (!application.ExecutableExists)
            {
                await _logging.LogWarningAsync($"Application executable not found: {application.ExecutablePath}", 
                    "ExternalApplicationService");
                return false;
            }
            
            if (!application.AllowMultipleInstances && application.IsRunning)
            {
                await _logging.LogWarningAsync($"Application {application.Name} is already running", 
                    "ExternalApplicationService");
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
                    application.AddProcessId(process.Id);
                    application.LastLaunched = DateTime.Now;
                    
                    await _logging.LogInfoAsync($"Successfully launched {application.Name} (PID: {process.Id})", 
                        "ExternalApplicationService");
                    
                    ApplicationStatusChanged?.Invoke(this, application);
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                await _logging.LogErrorAsync($"Error launching {application.Name}", ex, "ExternalApplicationService");
                return false;
            }
        }
        
        public async Task<bool> KillApplicationAsync(ExternalApplication application)
        {
            if (application == null || !application.IsRunning)
                return false;
            
            await _logging.LogInfoAsync($"Killing application: {application.Name}", "ExternalApplicationService");
            
            try
            {
                var processManagement = ServiceLocator.ProcessManagementService;
                bool anyFailed = false;
                
                var pids = application.ProcessIds.ToList();
                foreach (var pid in pids)
                {
                    var success = await processManagement.KillProcessAsync(pid, 5000);
                    if (!success)
                    {
                        anyFailed = true;
                    }
                    else
                    {
                        application.RemoveProcessId(pid);
                    }
                }
                
                if (!application.IsRunning)
                {
                    await _logging.LogInfoAsync($"Successfully killed {application.Name}", "ExternalApplicationService");
                    ApplicationStatusChanged?.Invoke(this, application);
                }
                
                return !anyFailed;
            }
            catch (Exception ex)
            {
                await _logging.LogErrorAsync($"Error killing {application.Name}", ex, "ExternalApplicationService");
                return false;
            }
        }
        
        public async Task RefreshApplicationStatusAsync()
        {
            await _logging.LogDebugAsync("Refreshing all application statuses", "ExternalApplicationService");
            
            List<ExternalApplication> apps;
            lock (_lock)
            {
                apps = _applications.ToList();
            }
            
            foreach (var app in apps)
            {
                await RefreshApplicationStatusAsync(app);
            }
        }
        
        public async Task RefreshApplicationStatusAsync(ExternalApplication application)
        {
            if (application == null) return;
            
            try
            {
                // Get the monitor ID for this application
                Guid? monitorId = null;
                lock (_lock)
                {
                    monitorId = _monitorToAppMap.FirstOrDefault(kvp => kvp.Value == application).Key;
                }
                
                if (monitorId == null) return;
                
                // Get processes from unified monitoring
                var processes = await _unifiedMonitoring.GetProcessesAsync(monitorId.Value);
                
                var wasRunning = application.IsRunning;
                
                // Update application process list
                application.SetProcessIds(processes.Select(p => p.ProcessId));
                application.CurrentInstances = processes.Count;
                
                // Fire event if status changed
                if (wasRunning != application.IsRunning)
                {
                    application.OnPropertyChanged(nameof(application.IsRunning));
                    application.OnPropertyChanged(nameof(application.StatusColor));
                    application.OnPropertyChanged(nameof(application.StatusText));
                    
                    await _logging.LogInfoAsync($"Application {application.Name} status changed: {(application.IsRunning ? "STARTED" : "STOPPED")}", 
                        "ExternalApplicationService");
                    
                    ApplicationStatusChanged?.Invoke(this, application);
                }
            }
            catch (Exception ex)
            {
                await _logging.LogWarningAsync($"Error refreshing status for {application.Name}: {ex.Message}", 
                    "ExternalApplicationService");
            }
        }
        
        public void StartMonitoring()
        {
            if (_isMonitoring) return;
            
            _isMonitoring = true;
            
            // Start unified monitoring if not already started
            if (!_unifiedMonitoring.IsMonitoring)
            {
                _unifiedMonitoring.StartMonitoring();
            }
            
            // Initial status refresh
            _ = RefreshApplicationStatusAsync();
            
            _ = _logging.LogInfoAsync("Started application monitoring", "ExternalApplicationService");
        }
        
        public void StopMonitoring()
        {
            if (!_isMonitoring) return;
            
            _isMonitoring = false;
            
            // We don't stop unified monitoring as other services might be using it
            
            _ = _logging.LogInfoAsync("Stopped application monitoring", "ExternalApplicationService");
        }
        
        private void RegisterApplicationProfiles()
        {
            lock (_lock)
            {
                foreach (var app in _applications)
                {
                    RegisterApplicationProfile(app);
                }
            }
        }
        
        private void RegisterApplicationProfile(ExternalApplication app)
        {
            if (string.IsNullOrWhiteSpace(app.ExecutablePath)) return;
            
            var processName = FFXIManager.Utilities.ProcessFilters.ExtractProcessName(app.ExecutablePath);
            if (string.IsNullOrWhiteSpace(processName)) return;
            
            var profile = new MonitoringProfile
            {
                Name = $"External App: {app.Name}",
                ProcessNames = new[] { processName },
                TrackWindows = false,        // Don't need windows
                TrackWindowTitles = false,   // Don't need titles
                IncludeProcessPath = true,   // Include path for validation
                Context = app                // Store app reference
            };
            
            var monitorId = _unifiedMonitoring.RegisterMonitor(profile);
            
            lock (_lock)
            {
                _monitorToAppMap[monitorId] = app;
            }
            
            _ = _logging.LogDebugAsync($"Registered monitoring profile for {app.Name} (Monitor: {monitorId})", 
                "ExternalApplicationService");
        }
        
        private void UpdateApplicationProfile(ExternalApplication app)
        {
            // Find existing monitor
            Guid? monitorId = null;
            lock (_lock)
            {
                monitorId = _monitorToAppMap.FirstOrDefault(kvp => kvp.Value == app).Key;
            }
            
            if (monitorId == null)
            {
                // No existing profile, register new one
                RegisterApplicationProfile(app);
                return;
            }
            
            // Update existing profile
            var processName = FFXIManager.Utilities.ProcessFilters.ExtractProcessName(app.ExecutablePath);
            if (string.IsNullOrWhiteSpace(processName)) return;
            
            var profile = new MonitoringProfile
            {
                Name = $"External App: {app.Name}",
                ProcessNames = new[] { processName },
                TrackWindows = false,
                TrackWindowTitles = false,
                IncludeProcessPath = true,
                Context = app
            };
            
            _unifiedMonitoring.UpdateMonitorProfile(monitorId.Value, profile);
        }
        
        private void UnregisterApplicationProfile(ExternalApplication app)
        {
            Guid? monitorId = null;
            lock (_lock)
            {
                monitorId = _monitorToAppMap.FirstOrDefault(kvp => kvp.Value == app).Key;
                if (monitorId != null)
                {
                    _monitorToAppMap.Remove(monitorId.Value);
                }
            }
            
            if (monitorId != null)
            {
                _unifiedMonitoring.UnregisterMonitor(monitorId.Value);
            }
        }
        
        private void OnProcessDetected(object? sender, MonitoredProcessEventArgs e)
        {
            if (!_isMonitoring) return;
            
            ExternalApplication? app = null;
            lock (_lock)
            {
                _monitorToAppMap.TryGetValue(e.MonitorId, out app);
            }
            
            if (app != null)
            {
                app.AddProcessId(e.Process.ProcessId);
                ApplicationStatusChanged?.Invoke(this, app);
                
                _ = _logging.LogInfoAsync($"Process detected for {app.Name}: PID {e.Process.ProcessId}", 
                    "ExternalApplicationService");
            }
        }
        
        private void OnProcessUpdated(object? sender, MonitoredProcessEventArgs e)
        {
            // We don't need to handle updates for external applications
        }
        
        private void OnProcessRemoved(object? sender, MonitoredProcessEventArgs e)
        {
            if (!_isMonitoring) return;
            
            ExternalApplication? app = null;
            lock (_lock)
            {
                _monitorToAppMap.TryGetValue(e.MonitorId, out app);
            }
            
            if (app != null)
            {
                app.RemoveProcessId(e.Process.ProcessId);
                ApplicationStatusChanged?.Invoke(this, app);
                
                _ = _logging.LogInfoAsync($"Process terminated for {app.Name}: PID {e.Process.ProcessId}", 
                    "ExternalApplicationService");
            }
        }
        
        private void LoadApplicationsFromSettings()
        {
            try
            {
                var settings = _settings.LoadSettings();
                
                if (settings.ExternalApplications != null && settings.ExternalApplications.Count > 0)
                {
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
                    
                    _ = _logging.LogInfoAsync($"Loaded {_applications.Count} applications from settings", 
                        "ExternalApplicationService");
                }
                else
                {
                    // Load defaults on first run
                    _applications.AddRange(GetDefaultApplications());
                    _ = _logging.LogInfoAsync("First run - loaded default applications", "ExternalApplicationService");
                    
                    // Save defaults
                    _ = Task.Run(async () => await SaveApplicationsToSettings());
                }
            }
            catch (Exception ex)
            {
                _applications.Clear();
                _applications.AddRange(GetDefaultApplications());
                _ = _logging.LogErrorAsync("Failed to load applications from settings", ex, "ExternalApplicationService");
            }
        }
        
        private async Task SaveApplicationsToSettings()
        {
            try
            {
                var settings = _settings.LoadSettings();
                
                List<ExternalApplication> apps;
                lock (_lock)
                {
                    apps = _applications.ToList();
                }
                
                settings.ExternalApplications = apps.Select(app => new ExternalApplicationData
                {
                    Name = app.Name,
                    ExecutablePath = app.ExecutablePath,
                    Arguments = app.Arguments,
                    WorkingDirectory = app.WorkingDirectory,
                    Description = app.Description,
                    IsEnabled = app.IsEnabled,
                    AllowMultipleInstances = app.AllowMultipleInstances
                }).ToList();
                
                _settings.SaveSettings(settings);
                
                await _logging.LogInfoAsync($"Saved {apps.Count} applications to settings", "ExternalApplicationService");
            }
            catch (Exception ex)
            {
                await _logging.LogErrorAsync("Failed to save applications to settings", ex, "ExternalApplicationService");
            }
        }
        
        private static List<ExternalApplication> GetDefaultApplications()
        {
            var applications = new List<ExternalApplication>();
            
            var potentialPaths = new Dictionary<string, string[]>
            {
                ["POL Proxy"] = new[]
                {
                    @"C:\Program Files\POLProxy\POLProxy.exe",
                    @"C:\Program Files (x86)\POLProxy\POLProxy.exe"
                },
                ["Windower"] = new[]
                {
                    @"C:\Windower4\Windower.exe",
                    @"C:\Program Files\Windower4\Windower.exe"
                },
                ["Silmaril"] = new[]
                {
                    @"C:\Program Files\Silmaril\Silmaril.exe",
                    @"C:\Silmaril\Silmaril.exe"
                }
            };
            
            foreach (var app in potentialPaths)
            {
                var foundPath = app.Value.FirstOrDefault(File.Exists) ?? app.Value[0];
                
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
                        application.AllowMultipleInstances = true;
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
            if (_disposed) return;
            _disposed = true;
            
            StopMonitoring();
            
            // Unregister all monitoring profiles
            List<Guid> monitorIds;
            lock (_lock)
            {
                monitorIds = _monitorToAppMap.Keys.ToList();
            }
            
            foreach (var monitorId in monitorIds)
            {
                _unifiedMonitoring.UnregisterMonitor(monitorId);
            }
            
            // Unsubscribe from events
            _unifiedMonitoring.ProcessDetected -= OnProcessDetected;
            _unifiedMonitoring.ProcessUpdated -= OnProcessUpdated;
            _unifiedMonitoring.ProcessRemoved -= OnProcessRemoved;
            
            GC.SuppressFinalize(this);
        }
    }
}

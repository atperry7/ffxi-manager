using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using FFXIManager.Infrastructure;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// ViewModel for the status bar showing operational status and contextual information
    /// </summary>
    public class StatusBarViewModel : ViewModelBase, IDisposable
    {
        private readonly IStatusMessageService _statusService;
        private readonly ISettingsService _settingsService;
        private readonly IProfileService _profileService;
        private readonly ILoggingService _loggingService;
        private readonly DispatcherTimer _refreshTimer;

        private string _statusMessage = "Ready";
        private string _lastRefreshTime = "Never";
        private string _diskSpaceInfo = "";
        private string _backupInfo = "";
        private bool _isLoading;
        private bool _disposed;
        private string _appVersion = string.Empty;

        public StatusBarViewModel(
            IStatusMessageService statusService,
            ISettingsService settingsService,
            IProfileService profileService,
            ILoggingService loggingService)
        {
            _statusService = statusService;
            _settingsService = settingsService;
            _profileService = profileService;
            _loggingService = loggingService;

            // Subscribe to status message changes
            _statusService.MessageChanged += OnStatusMessageChanged;

            // Set up refresh timer for contextual info
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _refreshTimer.Tick += RefreshContextualInfo;
            _refreshTimer.Start();

            // Compute app version once
            try
            {
                var asm = System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly();
                var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location);
                // Prefer ProductVersion; fall back to FileVersion; then AssemblyVersion
                var raw = fvi.ProductVersion;
                if (string.IsNullOrWhiteSpace(raw)) raw = fvi.FileVersion;
                if (string.IsNullOrWhiteSpace(raw)) raw = asm.GetName().Version?.ToString();
                _appVersion = NormalizeVersion(raw) ?? "dev";
            }
            catch
            {
                _appVersion = "dev";
            }

            // Initial refresh
            RefreshContextualInfo(null, null);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string LastRefreshTime
        {
            get => _lastRefreshTime;
            set => SetProperty(ref _lastRefreshTime, value);
        }

        public string DiskSpaceInfo
        {
            get => _diskSpaceInfo;
            set => SetProperty(ref _diskSpaceInfo, value);
        }

        public string BackupInfo
        {
            get => _backupInfo;
            set => SetProperty(ref _backupInfo, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string AppVersion => _appVersion;

        private void OnStatusMessageChanged(object? sender, string message)
        {
            StatusMessage = string.IsNullOrWhiteSpace(message) ? "Ready" : message;

            // Show loading indicator for certain operations
            IsLoading = message.Contains("Loading", StringComparison.OrdinalIgnoreCase) ||
                       message.Contains("Saving", StringComparison.OrdinalIgnoreCase) ||
                       message.Contains("Processing", StringComparison.OrdinalIgnoreCase) ||
                       message.Contains("Refreshing", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshContextualInfo(object? sender, EventArgs? e)
        {
            try
            {
                // Update last refresh time
                LastRefreshTime = $"Updated: {DateTime.Now:HH:mm:ss}";

                // Update disk space info
                var settings = _settingsService.LoadSettings();
                if (!string.IsNullOrEmpty(settings.PlayOnlineDirectory))
                {
                    try
                    {
                        var driveInfo = new DriveInfo(Path.GetPathRoot(settings.PlayOnlineDirectory) ?? "C:\\");
                        var freeGb = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                        DiskSpaceInfo = $"Free: {freeGb:F1} GB";
                    }
                    catch
                    {
                        DiskSpaceInfo = "Disk: N/A";
                    }
                }

                // Update backup count info
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var profiles = await _profileService.GetProfilesAsync();
                        var backupCount = profiles.Count(p => p.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase));
                        var userCount = profiles.Count(p => !p.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase) && !p.IsSystemFile);
                        BackupInfo = $"Profiles: {userCount} | Backups: {backupCount}";
                    }
                    catch
                    {
                        BackupInfo = "Profiles: N/A";
                    }
                });
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogDebugAsync($"Error refreshing status bar info: {ex.Message}");
            }
        }

        private static string? NormalizeVersion(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return v;
            // Strip build metadata suffix (SemVer '+...') and any whitespace-only suffixes
            var plusIdx = v.IndexOf('+');
            if (plusIdx >= 0)
            {
                v = v.Substring(0, plusIdx);
            }
            // Also trim any trailing space-separated commit-ish (defensive)
            var spaceIdx = v.IndexOf(' ');
            if (spaceIdx > 0)
            {
                v = v.Substring(0, spaceIdx);
            }
            return v.Trim();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _refreshTimer?.Stop();
            _statusService.MessageChanged -= OnStatusMessageChanged;

            GC.SuppressFinalize(this);
        }
    }
}

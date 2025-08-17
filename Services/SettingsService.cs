using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models.Settings;

namespace FFXIManager.Services
{
    /// <summary>
    /// Enhanced settings service with caching, atomic saves, and debounced operations
    /// </summary>
    public class SettingsService : ISettingsService, IDisposable
    {
        private const string SETTINGS_FILE = "FFXIManagerSettings.json";
        private const string BACKUP_FILE = "FFXIManagerSettings.json.bak";
        private const int DEBOUNCE_DELAY_MS = 1000; // 1 second debounce

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions { WriteIndented = true };

        private readonly string _settingsPath;
        private readonly string _backupPath;
        private readonly SemaphoreSlim _writeSemaphore = new SemaphoreSlim(1, 1);
        private Timer? _saveTimer;
        private ApplicationSettings? _cachedSettings;
        private ApplicationSettings? _pendingSaveSettings;
        private bool _disposed;

        public SettingsService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "FFXIManager");
            Directory.CreateDirectory(appFolder);
            _settingsPath = Path.Combine(appFolder, SETTINGS_FILE);
            _backupPath = Path.Combine(appFolder, BACKUP_FILE);
        }

        public ApplicationSettings LoadSettings()
        {
            // Return cached settings if available
            if (_cachedSettings != null)
            {
                return _cachedSettings;
            }

            ApplicationSettings? settings = null;

            // Try to load from primary settings file
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    settings = JsonSerializer.Deserialize<ApplicationSettings>(json);
                }
            }
            catch (Exception)
            {
                // Primary file failed, try backup
                try
                {
                    if (File.Exists(_backupPath))
                    {
                        var json = File.ReadAllText(_backupPath);
                        settings = JsonSerializer.Deserialize<ApplicationSettings>(json);
                    }
                }
                catch (Exception)
                {
                    // Both files failed, will use defaults
                }
            }

            // Use defaults if loading failed
            settings ??= new ApplicationSettings();

            // Cache the loaded settings
            _cachedSettings = settings;

            // If we loaded default settings and no file exists, create the initial file
            // This ensures that future atomic operations will work properly
            if (!File.Exists(_settingsPath) && !_disposed)
            {
                // Trigger immediate save (without debouncing) to create the initial file
                _pendingSaveSettings = _cachedSettings;
                _ = Task.Run(() => AtomicSave(_cachedSettings));
            }

            return _cachedSettings;
        }

        public void SaveSettings(ApplicationSettings settings)
        {
            if (_disposed)
                return;

            // Update cached settings
            _cachedSettings = settings;
            _pendingSaveSettings = settings;

            // Reset the debounce timer
            _saveTimer?.Dispose();
            _saveTimer = new Timer(DebouncedSave, null, DEBOUNCE_DELAY_MS, Timeout.Infinite);
        }

        public void UpdateTheme(bool isDarkTheme, double characterMonitorOpacity)
        {
            if (_disposed)
                return;

            // Load current settings if not cached
            var settings = _cachedSettings ?? LoadSettings();

            // Update theme properties
            settings.IsDarkTheme = isDarkTheme;
            settings.CharacterMonitorOpacity = characterMonitorOpacity;

            // Update cached settings
            _cachedSettings = settings;
            _pendingSaveSettings = settings;

            // Reset the debounce timer to schedule a write
            _saveTimer?.Dispose();
            _saveTimer = new Timer(DebouncedSave, null, DEBOUNCE_DELAY_MS, Timeout.Infinite);
        }

        public void UpdateWindowBounds(double width, double height, double left, double top, bool isMaximized, bool rememberPosition)
        {
            if (_disposed)
                return;

            // Load current settings if not cached
            var settings = _cachedSettings ?? LoadSettings();

            // Update window bounds properties
            settings.MainWindowWidth = width;
            settings.MainWindowHeight = height;
            settings.MainWindowLeft = left;
            settings.MainWindowTop = top;
            settings.MainWindowMaximized = isMaximized;
            settings.RememberWindowPosition = rememberPosition;

            // Update cached settings
            _cachedSettings = settings;
            _pendingSaveSettings = settings;

            // Reset the debounce timer to schedule a write
            _saveTimer?.Dispose();
            _saveTimer = new Timer(DebouncedSave, null, DEBOUNCE_DELAY_MS, Timeout.Infinite);
        }

        public void UpdateDiagnostics(bool enableDiagnostics, bool verboseLogging, int maxLogEntries)
        {
            if (_disposed)
                return;

            // Load current settings if not cached
            var settings = _cachedSettings ?? LoadSettings();

            // Update diagnostics properties
            settings.Diagnostics ??= new DiagnosticsOptions();
            settings.Diagnostics.EnableDiagnostics = enableDiagnostics;
            settings.Diagnostics.VerboseLogging = verboseLogging;
            settings.Diagnostics.MaxLogEntries = maxLogEntries;

            // Update cached settings
            _cachedSettings = settings;
            _pendingSaveSettings = settings;

            // Reset the debounce timer to schedule a write
            _saveTimer?.Dispose();
            _saveTimer = new Timer(DebouncedSave, null, DEBOUNCE_DELAY_MS, Timeout.Infinite);
        }

        private void DebouncedSave(object? state)
        {
            if (_disposed || _pendingSaveSettings == null)
                return;

            var settingsToSave = _pendingSaveSettings;
            _pendingSaveSettings = null;

            // Perform atomic save in background
            _ = Task.Run(() => AtomicSave(settingsToSave));
        }

        private async Task AtomicSave(ApplicationSettings settings)
        {
            if (_disposed)
                return;

            await _writeSemaphore.WaitAsync();
            try
            {
                var json = JsonSerializer.Serialize(settings, SerializerOptions);
                var tempPath = _settingsPath + ".tmp";

                // Write to temporary file with retry logic
                await RetryFileOperationAsync(async () => await File.WriteAllTextAsync(tempPath, json));

                // Backup existing settings file if it exists
                if (File.Exists(_settingsPath))
                {
                    await RetryFileOperationAsync(() =>
                    {
                        File.Copy(_settingsPath, _backupPath, true);
                        return Task.CompletedTask;
                    });
                }

                // Atomically replace or move the settings file
                await RetryFileOperationAsync(() =>
                {
                    if (File.Exists(_settingsPath))
                    {
                        // Target exists, use atomic replace
                        File.Replace(tempPath, _settingsPath, null);
                    }
                    else
                    {
                        // Target doesn't exist, use move (effectively atomic on same volume)
                        File.Move(tempPath, _settingsPath);
                    }
                    return Task.CompletedTask;
                });
            }
            catch (Exception)
            {
                // Log the error but don't crash the application
                // Clean up temp file if it exists
                try
                {
                    var tempPath = _settingsPath + ".tmp";
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }

                // For debugging purposes, you could add logging here if ILoggingService was available
                // The settings service intentionally doesn't take logging dependencies to avoid circular references
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        /// <summary>
        /// Retries file operations with exponential backoff for common file system issues
        /// </summary>
        private static async Task RetryFileOperationAsync(Func<Task> operation, int maxRetries = 5, int initialDelayMs = 50)
        {
            var delay = initialDelayMs;
            Exception? lastException = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await operation();
                    return; // Success
                }
                catch (IOException ex) when (attempt < maxRetries)
                {
                    lastException = ex;
                    await Task.Delay(delay);
                    delay = Math.Min(delay * 2, 1000); // Cap at 1 second
                }
                catch (UnauthorizedAccessException ex) when (attempt < maxRetries)
                {
                    lastException = ex;
                    await Task.Delay(delay);
                    delay = Math.Min(delay * 2, 1000);
                }
                catch (DirectoryNotFoundException ex) when (attempt < maxRetries)
                {
                    lastException = ex;
                    // Recreate the directory if it was deleted
                    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var appFolder = Path.Combine(appDataPath, "FFXIManager");
                    Directory.CreateDirectory(appFolder);
                    await Task.Delay(delay);
                    delay = Math.Min(delay * 2, 1000);
                }
            }

            // If we've exhausted retries, throw the last exception
            throw lastException ?? new InvalidOperationException("File operation failed after retries");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Dispose the timer and wait for any pending save
            _saveTimer?.Dispose();

            // If there's a pending save, perform it synchronously before disposing
            if (_pendingSaveSettings != null)
            {
                try
                {
                    AtomicSave(_pendingSaveSettings).Wait(5000); // Wait up to 5 seconds
                }
                catch (Exception)
                {
                    // Ignore errors during disposal
                }
            }

            _writeSemaphore?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Simple application settings model
    /// </summary>
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FFXIManager.Models.Settings;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for logging operations
    /// </summary>
    public interface ILoggingService
    {
        Task LogInfoAsync(string message, string? category = null);
        Task LogWarningAsync(string message, string? category = null);
        Task LogErrorAsync(string message, Exception? exception = null, string? category = null);
        Task LogDebugAsync(string message, string? category = null);
        Task<List<LogEntry>> GetRecentLogsAsync(int count = 100);
        Task ClearLogsAsync();
    }

    /// <summary>
    /// Log entry model
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? Exception { get; set; }
    }

    /// <summary>
    /// Log levels for categorizing log entries
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// File-based logging service with JSON persistence
    /// </summary>
    public class LoggingService : ILoggingService
    {
        private readonly string _logFilePath;
        private int _maxLogEntries;
        private readonly List<LogEntry> _logBuffer = new();
        private readonly object _lock = new();
        private readonly ISettingsService? _settingsService;

        public LogLevel MinimumLevel { get; private set; } = LogLevel.Info;

        public LoggingService() : this(null) { }

        public LoggingService(ISettingsService? settingsService)
        {
            _settingsService = settingsService;
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDirectory = Path.Combine(appDataPath, "FFXIManager", "Logs");
            Directory.CreateDirectory(logDirectory);
            _logFilePath = Path.Combine(logDirectory, $"FFXIManager_{DateTime.Now:yyyyMMdd}.log");
            // Initialize from settings if available
            ApplySettings();
        }

        private void ApplySettings()
        {
            try
            {
                var settings = _settingsService?.LoadSettings();
                if (settings != null)
                {
                    var diag = settings.Diagnostics ?? new DiagnosticsOptions();
                    _maxLogEntries = Math.Max(100, Math.Min(diag.MaxLogEntries, 100000));
                    // Determine minimum level based on diagnostics toggles
                    if (!diag.EnableDiagnostics)
                    {
                        MinimumLevel = LogLevel.Warning;
                    }
                    else if (diag.VerboseLogging)
                    {
                        MinimumLevel = LogLevel.Debug;
                    }
                    else
                    {
                        MinimumLevel = LogLevel.Info;
                    }
                }
                else
                {
                    _maxLogEntries = 1000;
                    MinimumLevel = LogLevel.Info;
                }
            }
            catch
            {
                _maxLogEntries = 1000;
                MinimumLevel = LogLevel.Info;
            }
        }

        public async Task LogInfoAsync(string message, string? category = null)
        {
            await LogAsync(LogLevel.Info, message, null, category);
        }

        public async Task LogWarningAsync(string message, string? category = null)
        {
            await LogAsync(LogLevel.Warning, message, null, category);
        }

        public async Task LogErrorAsync(string message, Exception? exception = null, string? category = null)
        {
            await LogAsync(LogLevel.Error, message, exception, category);
        }

        public async Task LogDebugAsync(string message, string? category = null)
        {
            await LogAsync(LogLevel.Debug, message, null, category);
        }

        public async Task<List<LogEntry>> GetRecentLogsAsync(int count = 100)
        {
            await LoadLogsIfNeeded();

            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    var startIndex = Math.Max(0, _logBuffer.Count - count);
                    return _logBuffer.GetRange(startIndex, _logBuffer.Count - startIndex);
                }
            });
        }

        public Task ClearLogsAsync()
        {
            lock (_lock)
            {
                _logBuffer.Clear();
            }

            try
            {
                if (File.Exists(_logFilePath))
                {
                    File.Delete(_logFilePath);
                }
            }
            catch
            {
                // Ignore file deletion errors
            }

            return Task.CompletedTask;
        }

        private Task LogAsync(LogLevel level, string message, Exception? exception, string? category)
        {
            // Refresh settings-derived thresholds lazily to reflect latest toggles
            ApplySettings();

            if (level < MinimumLevel)
            {
                // Suppress detailed events when below threshold
                return Task.CompletedTask;
            }

            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Category = category,
                Exception = exception?.ToString()
            };

            lock (_lock)
            {
                _logBuffer.Add(logEntry);

                // Keep buffer size manageable
                if (_logBuffer.Count > _maxLogEntries)
                {
                    _logBuffer.RemoveRange(0, _logBuffer.Count - _maxLogEntries);
                }
            }

            // Write to file asynchronously (fire-and-forget)
            _ = Task.Run(async () => await WriteToFileAsync(logEntry));

            return Task.CompletedTask;
        }

        private async Task WriteToFileAsync(LogEntry logEntry)
        {
            try
            {
                var logLine = $"{logEntry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{logEntry.Level}] {logEntry.Category ?? "General"}: {logEntry.Message}";
                if (!string.IsNullOrEmpty(logEntry.Exception))
                {
                    logLine += Environment.NewLine + $"Exception: {logEntry.Exception}";
                }
                logLine += Environment.NewLine;

                await File.AppendAllTextAsync(_logFilePath, logLine);
            }
            catch
            {
                // Ignore file write errors - logging should not break the application
            }
        }

        private async Task LoadLogsIfNeeded()
        {
            lock (_lock)
            {
                if (_logBuffer.Count > 0) return; // Already loaded
            }

            if (!File.Exists(_logFilePath)) return;

            try
            {
                var lines = await File.ReadAllLinesAsync(_logFilePath);
                var entries = new List<LogEntry>();

                foreach (var line in lines)
                {
                    if (TryParseLogLine(line, out var entry))
                    {
                        entries.Add(entry);
                    }
                }

                lock (_lock)
                {
                    _logBuffer.AddRange(entries);
                }
            }
            catch
            {
                // Ignore file read errors
            }
        }

        private static bool TryParseLogLine(string line, out LogEntry entry)
        {
            entry = new LogEntry();

            try
            {
                // Simple parsing - could be enhanced with regex for better accuracy
                if (line.Length < 24) return false;

                var timestampStr = line.Substring(0, 23);
                if (!DateTime.TryParse(timestampStr, out var timestamp)) return false;

                var levelStart = line.IndexOf('[') + 1;
                var levelEnd = line.IndexOf(']');
                if (levelStart <= 0 || levelEnd <= levelStart) return false;

                var levelStr = line.Substring(levelStart, levelEnd - levelStart);
                if (!Enum.TryParse<LogLevel>(levelStr, out var level)) return false;

                var messageStart = line.IndexOf(':', levelEnd) + 1;
                if (messageStart <= 0) return false;

                entry.Timestamp = timestamp;
                entry.Level = level;
                entry.Message = line.Substring(messageStart).Trim();

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

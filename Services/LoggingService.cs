using Microsoft.Extensions.Logging;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for logging operations
    /// </summary>
    public interface ILoggingService
    {
        // Structured logging methods with message templates
        Task LogInfoAsync(string messageTemplate, params object[] args);
        Task LogInfoAsync(string messageTemplate, string? category, params object[] args);
        Task LogWarningAsync(string messageTemplate, params object[] args);
        Task LogWarningAsync(string messageTemplate, string? category, params object[] args);
        Task LogErrorAsync(string messageTemplate, Exception? exception = null, params object[] args);
        Task LogErrorAsync(string messageTemplate, string? category, Exception? exception = null, params object[] args);
        Task LogDebugAsync(string messageTemplate, params object[] args);
        Task LogDebugAsync(string messageTemplate, string? category, params object[] args);

        // Legacy methods for backward compatibility
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
        public FFXIManagerLogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? Exception { get; set; }
    }

    /// <summary>
    /// Log levels for categorizing log entries (renamed to avoid conflicts)
    /// </summary>
    public enum FFXIManagerLogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Serilog-based logging service adapter that implements ILoggingService
    /// </summary>
    public class LoggingService : ILoggingService
    {
        private readonly ILogger<LoggingService> _logger;
        private readonly ISettingsService? _settingsService;
        private readonly List<LogEntry> _logBuffer = new();
        private readonly object _lock = new();
        private int _maxLogEntries = 1000;

        public LoggingService(ILogger<LoggingService> logger, ISettingsService? settingsService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsService = settingsService;
            ApplySettings();
        }

        private void ApplySettings()
        {
            try
            {
                var settings = _settingsService?.LoadSettings();
                if (settings?.Diagnostics != null)
                {
                    var diag = settings.Diagnostics;
                    _maxLogEntries = Math.Max(100, Math.Min(diag.MaxLogEntries, 100000));
                }
                else
                {
                    _maxLogEntries = 1000;
                }
            }
            catch
            {
                _maxLogEntries = 1000;
            }
        }

        // Structured logging implementations
        public Task LogInfoAsync(string messageTemplate, params object[] args)
        {
            _logger.LogInformation(messageTemplate, args);
            AddToBuffer(FFXIManagerLogLevel.Info, SafeFormat(messageTemplate, args), null, null);
            return Task.CompletedTask;
        }

        public Task LogInfoAsync(string messageTemplate, string? category, params object[] args)
        {
            using var scope = !string.IsNullOrEmpty(category) ? _logger.BeginScope(new Dictionary<string, object> { { "Category", category } }) : null;
            _logger.LogInformation(messageTemplate, args);
            AddToBuffer(FFXIManagerLogLevel.Info, SafeFormat(messageTemplate, args), null, category);
            return Task.CompletedTask;
        }

        // Legacy method for backward compatibility
        public Task LogInfoAsync(string message, string? category = null)
        {
            using var scope = !string.IsNullOrEmpty(category) ? _logger.BeginScope(new Dictionary<string, object> { { "Category", category } }) : null;
            _logger.LogInformation("{Message}", message);
            AddToBuffer(FFXIManagerLogLevel.Info, message, null, category);
            return Task.CompletedTask;
        }

        public Task LogWarningAsync(string messageTemplate, params object[] args)
        {
            _logger.LogWarning(messageTemplate, args);
            AddToBuffer(FFXIManagerLogLevel.Warning, SafeFormat(messageTemplate, args), null, null);
            return Task.CompletedTask;
        }

        public Task LogWarningAsync(string messageTemplate, string? category, params object[] args)
        {
            using var scope = !string.IsNullOrEmpty(category) ? _logger.BeginScope(new Dictionary<string, object> { { "Category", category } }) : null;
            _logger.LogWarning(messageTemplate, args);
            AddToBuffer(FFXIManagerLogLevel.Warning, SafeFormat(messageTemplate, args), null, category);
            return Task.CompletedTask;
        }

        // Legacy method for backward compatibility
        public Task LogWarningAsync(string message, string? category = null)
        {
            using var scope = !string.IsNullOrEmpty(category) ? _logger.BeginScope(new Dictionary<string, object> { { "Category", category } }) : null;
            _logger.LogWarning("{Message}", message);
            AddToBuffer(FFXIManagerLogLevel.Warning, message, null, category);
            return Task.CompletedTask;
        }

        public Task LogErrorAsync(string messageTemplate, Exception? exception = null, params object[] args)
        {
            _logger.LogError(exception, messageTemplate, args);
            AddToBuffer(FFXIManagerLogLevel.Error, SafeFormat(messageTemplate, args), exception, null);
            return Task.CompletedTask;
        }

        public Task LogErrorAsync(string messageTemplate, string? category, Exception? exception = null, params object[] args)
        {
            using var scope = !string.IsNullOrEmpty(category) ? _logger.BeginScope(new Dictionary<string, object> { { "Category", category } }) : null;
            _logger.LogError(exception, messageTemplate, args);
            AddToBuffer(FFXIManagerLogLevel.Error, SafeFormat(messageTemplate, args), exception, category);
            return Task.CompletedTask;
        }

        // Legacy method for backward compatibility
        public Task LogErrorAsync(string message, Exception? exception = null, string? category = null)
        {
            using var scope = !string.IsNullOrEmpty(category) ? _logger.BeginScope(new Dictionary<string, object> { { "Category", category } }) : null;
            _logger.LogError(exception, "{Message}", message);
            AddToBuffer(FFXIManagerLogLevel.Error, message, exception, category);
            return Task.CompletedTask;
        }

        public Task LogDebugAsync(string messageTemplate, params object[] args)
        {
            _logger.LogDebug(messageTemplate, args);
            AddToBuffer(FFXIManagerLogLevel.Debug, SafeFormat(messageTemplate, args), null, null);
            return Task.CompletedTask;
        }

        public Task LogDebugAsync(string messageTemplate, string? category, params object[] args)
        {
            using var scope = !string.IsNullOrEmpty(category) ? _logger.BeginScope(new Dictionary<string, object> { { "Category", category } }) : null;
            _logger.LogDebug(messageTemplate, args);
            AddToBuffer(FFXIManagerLogLevel.Debug, SafeFormat(messageTemplate, args), null, category);
            return Task.CompletedTask;
        }

        private static string SafeFormat(string messageTemplate, object[] args)
        {
            if (args == null || args.Length == 0) return messageTemplate;
            try
            {
                return string.Format(messageTemplate, args);
            }
            catch (FormatException)
            {
                // Fallback for named templates like "Processed {Count} items";
                // append args for readability without throwing.
                try
                {
                    var renderedArgs = string.Join(", ", args.Select(a => a?.ToString() ?? "null"));
                    return $"{messageTemplate} | Args: {renderedArgs}";
                }
                catch
                {
                    return messageTemplate;
                }
            }
        }

        // Legacy method for backward compatibility
        public Task LogDebugAsync(string message, string? category = null)
        {
            using var scope = !string.IsNullOrEmpty(category) ? _logger.BeginScope(new Dictionary<string, object> { { "Category", category } }) : null;
            _logger.LogDebug("{Message}", message);
            AddToBuffer(FFXIManagerLogLevel.Debug, message, null, category);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Adds a log entry to the in-memory buffer for GetRecentLogsAsync support
        /// </summary>
        private void AddToBuffer(FFXIManagerLogLevel level, string message, Exception? exception, string? category)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Category = category,
                Exception = exception?.ToString()
            };

            lock (_lock)
            {
                _logBuffer.Add(entry);

                // Keep buffer size manageable
                if (_logBuffer.Count > _maxLogEntries)
                {
                    _logBuffer.RemoveRange(0, _logBuffer.Count - _maxLogEntries);
                }
            }
        }

        public Task<List<LogEntry>> GetRecentLogsAsync(int count = 100)
        {
            return Task.Run(() =>
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

            // Note: We don't clear Serilog's files directly as they're managed by Serilog sinks
            // If file clearing is needed, it should be done through Serilog configuration
            _logger.LogInformation("Log buffer cleared via ClearLogsAsync");

            return Task.CompletedTask;
        }

    }
}

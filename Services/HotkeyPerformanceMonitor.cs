using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Infrastructure;

namespace FFXIManager.Services
{
    /// <summary>
    /// Performance monitoring service for hotkey activation pipeline.
    /// Collects metrics and provides diagnostics for gaming optimization.
    /// </summary>
    public interface IHotkeyPerformanceMonitor
    {
        /// <summary>
        /// Records a hotkey activation performance measurement.
        /// </summary>
        void RecordActivation(HotkeyActivationMetrics metrics);
        
        /// <summary>
        /// Gets current performance statistics.
        /// </summary>
        HotkeyPerformanceStats GetStatistics();
        
        /// <summary>
        /// Resets all performance counters.
        /// </summary>
        void ResetCounters();
        
        /// <summary>
        /// Gets detailed performance history for diagnostics.
        /// </summary>
        HotkeyPerformanceHistory GetPerformanceHistory(int maxEntries = 100);
        
        /// <summary>
        /// Event raised when performance thresholds are exceeded.
        /// </summary>
        event EventHandler<PerformanceThresholdEventArgs>? ThresholdExceeded;
    }

    /// <summary>
    /// Implementation of hotkey performance monitoring.
    /// </summary>
    public class HotkeyPerformanceMonitor : IHotkeyPerformanceMonitor, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly ConcurrentQueue<HotkeyActivationMetrics> _recentActivations = new();
        private readonly object _statsLock = new object();
        
        // Performance counters
        private int _totalActivations;
        private int _successfulActivations;
        private int _failedActivations;
        private double _totalActivationTimeMs;
        private double _minActivationTimeMs = double.MaxValue;
        private double _maxActivationTimeMs;
        
        // Settings-driven performance thresholds
        private double _warningThresholdMs = 50;  // Default: 50ms
        private double _criticalThresholdMs = 200; // Default: 200ms
        private int _recentActivationHistorySize = 1000; // Default: 1000
        
        public event EventHandler<PerformanceThresholdEventArgs>? ThresholdExceeded;

        public HotkeyPerformanceMonitor(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            
            // **SETTINGS-DRIVEN**: Load performance thresholds from configuration
            LoadPerformanceSettingsFromConfiguration();
            
            _ = _loggingService.LogInfoAsync($"HotkeyPerformanceMonitor initialized - Warning: {_warningThresholdMs}ms, Critical: {_criticalThresholdMs}ms", "HotkeyPerformanceMonitor");
        }

        /// <summary>
        /// Records a hotkey activation performance measurement.
        /// </summary>
        public void RecordActivation(HotkeyActivationMetrics metrics)
        {
            if (metrics == null) return;

            lock (_statsLock)
            {
                // Update counters
                Interlocked.Increment(ref _totalActivations);
                
                if (metrics.Success)
                {
                    Interlocked.Increment(ref _successfulActivations);
                }
                else
                {
                    Interlocked.Increment(ref _failedActivations);
                }

                // Update timing statistics
                var activationTime = metrics.TotalTimeMs;
                _totalActivationTimeMs += activationTime;
                
                if (activationTime < _minActivationTimeMs)
                {
                    _minActivationTimeMs = activationTime;
                }
                
                if (activationTime > _maxActivationTimeMs)
                {
                    _maxActivationTimeMs = activationTime;
                }

                // Store recent activation for history
                _recentActivations.Enqueue(metrics);
                
                // Limit history size
                while (_recentActivations.Count > _recentActivationHistorySize)
                {
                    _recentActivations.TryDequeue(out _);
                }

                // Check performance thresholds
                CheckPerformanceThresholds(metrics);
            }
        }

        /// <summary>
        /// Gets current performance statistics.
        /// </summary>
        public HotkeyPerformanceStats GetStatistics()
        {
            lock (_statsLock)
            {
                var successRate = _totalActivations > 0 
                    ? (_successfulActivations / (double)_totalActivations) * 100 
                    : 0;

                var avgActivationTime = _totalActivations > 0 
                    ? _totalActivationTimeMs / _totalActivations 
                    : 0;

                // Calculate 95th percentile from recent data
                var recentTimes = _recentActivations.Select(m => m.TotalTimeMs).OrderBy(t => t).ToList();
                var p95Index = (int)Math.Ceiling(recentTimes.Count * 0.95) - 1;
                var p95Time = recentTimes.Count > 0 && p95Index >= 0 ? recentTimes[Math.Min(p95Index, recentTimes.Count - 1)] : 0;

                return new HotkeyPerformanceStats
                {
                    TotalActivations = _totalActivations,
                    SuccessfulActivations = _successfulActivations,
                    FailedActivations = _failedActivations,
                    SuccessRate = successRate,
                    AverageActivationTimeMs = avgActivationTime,
                    MinActivationTimeMs = _minActivationTimeMs == double.MaxValue ? 0 : _minActivationTimeMs,
                    MaxActivationTimeMs = _maxActivationTimeMs,
                    P95ActivationTimeMs = p95Time,
                    RecentActivationCount = _recentActivations.Count
                };
            }
        }

        /// <summary>
        /// Resets all performance counters.
        /// </summary>
        public void ResetCounters()
        {
            lock (_statsLock)
            {
                _totalActivations = 0;
                _successfulActivations = 0;
                _failedActivations = 0;
                _totalActivationTimeMs = 0;
                _minActivationTimeMs = double.MaxValue;
                _maxActivationTimeMs = 0;
                
                while (_recentActivations.TryDequeue(out _)) { }
                
                _ = _loggingService.LogInfoAsync("Performance counters reset", "HotkeyPerformanceMonitor");
            }
        }

        /// <summary>
        /// Gets detailed performance history for diagnostics.
        /// </summary>
        public HotkeyPerformanceHistory GetPerformanceHistory(int maxEntries = 100)
        {
            var recentMetrics = _recentActivations
                .TakeLast(maxEntries)
                .ToList();

            var timingBreakdown = new HotkeyTimingBreakdown();
            
            if (recentMetrics.Count > 0)
            {
                timingBreakdown.AverageCharacterLookupMs = recentMetrics.Average(m => m.CharacterLookupTimeMs);
                timingBreakdown.AverageActivationTimeMs = recentMetrics.Average(m => m.WindowActivationTimeMs);
                timingBreakdown.AverageRetryTimeMs = recentMetrics.Where(m => m.RetryCount > 0).DefaultIfEmpty().Average(m => m?.RetryTimeMs ?? 0);
            }

            return new HotkeyPerformanceHistory
            {
                RecentActivations = recentMetrics,
                TimingBreakdown = timingBreakdown,
                GeneratedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Checks if activation time exceeds performance thresholds.
        /// </summary>
        private void CheckPerformanceThresholds(HotkeyActivationMetrics metrics)
        {
            var activationTime = metrics.TotalTimeMs;
            
            if (activationTime > _criticalThresholdMs)
            {
                var args = new PerformanceThresholdEventArgs
                {
                    ThresholdType = PerformanceThresholdType.Critical,
                    ActivationTimeMs = activationTime,
                    ThresholdMs = _criticalThresholdMs,
                    Metrics = metrics
                };
                
                ThresholdExceeded?.Invoke(this, args);
                
                _ = _loggingService.LogWarningAsync(
                    $"CRITICAL: Hotkey activation took {activationTime:F1}ms (threshold: {_criticalThresholdMs}ms) for character '{metrics.CharacterName}'", 
                    "HotkeyPerformanceMonitor");
            }
            else if (activationTime > _warningThresholdMs)
            {
                var args = new PerformanceThresholdEventArgs
                {
                    ThresholdType = PerformanceThresholdType.Warning,
                    ActivationTimeMs = activationTime,
                    ThresholdMs = _warningThresholdMs,
                    Metrics = metrics
                };
                
                ThresholdExceeded?.Invoke(this, args);
                
                _ = _loggingService.LogDebugAsync(
                    $"WARNING: Hotkey activation took {activationTime:F1}ms (threshold: {_warningThresholdMs}ms) for character '{metrics.CharacterName}'", 
                    "HotkeyPerformanceMonitor");
            }
        }

        /// <summary>
        /// Loads performance monitoring settings from application configuration
        /// </summary>
        private void LoadPerformanceSettingsFromConfiguration()
        {
            try
            {
                var settingsService = ServiceLocator.SettingsService;
                var settings = settingsService.LoadSettings();
                
                _warningThresholdMs = settings.PerformanceWarningThresholdMs;
                _criticalThresholdMs = settings.PerformanceCriticalThresholdMs;
                _recentActivationHistorySize = settings.PerformanceHistorySize;
                
                _ = _loggingService.LogDebugAsync($"Performance settings loaded - Warning: {_warningThresholdMs}ms, Critical: {_criticalThresholdMs}ms, History: {_recentActivationHistorySize}", "HotkeyPerformanceMonitor");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogWarningAsync($"Error loading performance settings, using defaults: {ex.Message}", "HotkeyPerformanceMonitor");
            }
        }

        public void Dispose()
        {
            // Clear event handlers
            ThresholdExceeded = null;
            
            _ = _loggingService?.LogInfoAsync("HotkeyPerformanceMonitor disposed", "HotkeyPerformanceMonitor");
            
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Metrics for a single hotkey activation.
    /// </summary>
    public class HotkeyActivationMetrics
    {
        public int HotkeyId { get; init; }
        public string CharacterName { get; init; } = string.Empty;
        public bool Success { get; init; }
        public double TotalTimeMs { get; init; }
        public double CharacterLookupTimeMs { get; init; }
        public double WindowActivationTimeMs { get; init; }
        public double RetryTimeMs { get; init; }
        public int RetryCount { get; init; }
        public string? ErrorMessage { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Overall performance statistics.
    /// </summary>
    public class HotkeyPerformanceStats
    {
        public int TotalActivations { get; init; }
        public int SuccessfulActivations { get; init; }
        public int FailedActivations { get; init; }
        public double SuccessRate { get; init; }
        public double AverageActivationTimeMs { get; init; }
        public double MinActivationTimeMs { get; init; }
        public double MaxActivationTimeMs { get; init; }
        public double P95ActivationTimeMs { get; init; }
        public int RecentActivationCount { get; init; }
    }

    /// <summary>
    /// Detailed performance history for diagnostics.
    /// </summary>
    public class HotkeyPerformanceHistory
    {
        public List<HotkeyActivationMetrics> RecentActivations { get; init; } = new();
        public HotkeyTimingBreakdown TimingBreakdown { get; init; } = new();
        public DateTime GeneratedAt { get; init; }
    }

    /// <summary>
    /// Breakdown of timing components.
    /// </summary>
    public class HotkeyTimingBreakdown
    {
        public double AverageCharacterLookupMs { get; set; }
        public double AverageActivationTimeMs { get; set; }
        public double AverageRetryTimeMs { get; set; }
    }

    /// <summary>
    /// Performance threshold event arguments.
    /// </summary>
    public class PerformanceThresholdEventArgs : EventArgs
    {
        public PerformanceThresholdType ThresholdType { get; init; }
        public double ActivationTimeMs { get; init; }
        public double ThresholdMs { get; init; }
        public HotkeyActivationMetrics Metrics { get; init; } = new();
    }

    /// <summary>
    /// Types of performance thresholds.
    /// </summary>
    public enum PerformanceThresholdType
    {
        Warning,
        Critical
    }
}

using System.Collections.Generic;
using System.Windows.Input;

namespace FFXIManager.Models.Settings
{
    /// <summary>
    /// Application settings model with migration support
    /// </summary>
    public class ApplicationSettings
    {
        /// <summary>
        /// Settings version for migration purposes. Current version: 2
        /// Version 0/1: Legacy settings (500ms or 25ms debounce)
        /// Version 2: Gaming-optimized with 5ms debounce for ultra-responsive performance
        /// </summary>
        public int SettingsVersion { get; set; } = 2;
        public string PlayOnlineDirectory { get; set; } = @"C:\Program Files (x86)\PlayOnline\SquareEnix\PlayOnlineViewer\usr\all";
        public bool AutoRefreshOnStartup { get; set; } = true;
        public bool ConfirmDeleteOperations { get; set; } = true;
        public bool CreateAutoBackups { get; set; } = true;
        public int MaxAutoBackups { get; set; } = 5;
        public bool ShowAutoBackupsInList { get; set; }
        public bool EnableSmartBackupDeduplication { get; set; } = true;

        // Theme and personalization
        public bool IsDarkTheme { get; set; } = true; // Default to dark theme
        public double CharacterMonitorOpacity { get; set; } = 0.95; // Default to 95% opacity

        // Profile tracking
        public string LastActiveProfileName { get; set; } = string.Empty;
        public string LastUsedProfile { get; set; } = string.Empty;

        // External applications persistence
        public List<ExternalApplicationData> ExternalApplications { get; set; } = new();

        // Diagnostics and logging options
        public DiagnosticsOptions Diagnostics { get; set; } = new DiagnosticsOptions();

        // Keyboard shortcuts for character switching
        public List<KeyboardShortcutConfig> CharacterSwitchShortcuts { get; set; } = new();

        /// <summary>
        /// Debounce interval in milliseconds to prevent accidental rapid hotkey presses.
        /// Optimized for gaming: 5ms provides ultra-responsive switching while preventing double-presses.
        /// </summary>
        public int HotkeyDebounceIntervalMs { get; set; } = 5;

        /// <summary>
        /// Activation debounce interval in milliseconds to prevent rapid character switching.
        /// Ultra-responsive gaming: 5ms provides near-instant switching while preventing double-activation.
        /// </summary>
        public int ActivationDebounceIntervalMs { get; set; } = 5;

        /// <summary>
        /// Minimum interval between activation attempts for the same character in milliseconds.
        /// 5ms prevents spam-clicking the same character while allowing ultra-fast switching between different characters.
        /// </summary>
        public int MinActivationIntervalMs { get; set; } = 5;

        /// <summary>
        /// Timeout for character window activation operations in milliseconds.
        /// Reduced from 8000ms to 3000ms for gaming responsiveness while allowing time for window switching.
        /// </summary>
        public int ActivationTimeoutMs { get; set; } = 3000;

        // Performance monitoring and tuning settings
        /// <summary>
        /// Performance warning threshold in milliseconds. Activations slower than this will generate warnings.
        /// Default: 25ms provides tight gaming-focused performance monitoring.
        /// </summary>
        public int PerformanceWarningThresholdMs { get; set; } = 25;

        /// <summary>
        /// Performance critical threshold in milliseconds. Activations slower than this will generate critical alerts.
        /// Default: 100ms indicates gaming performance degradation requiring attention.
        /// </summary>
        public int PerformanceCriticalThresholdMs { get; set; } = 100;

        /// <summary>
        /// Character cache validity period in milliseconds. Cached character data is refreshed after this interval.
        /// Default: 1500ms (1.5s) provides good balance between performance and data freshness.
        /// Lower values = more responsive to changes, higher values = better performance.
        /// </summary>
        public int CharacterCacheValidityMs { get; set; } = 1500;

        /// <summary>
        /// Automatic hotkey mapping refresh interval in milliseconds. Mappings are refreshed periodically.
        /// Default: 30000ms (30s) ensures mappings stay synchronized with character changes.
        /// Set to 0 to disable automatic refresh (manual refresh only).
        /// </summary>
        public int HotkeyMappingRefreshIntervalMs { get; set; } = 30000;

        /// <summary>
        /// Maximum number of characters to cache for performance optimization.
        /// Default: 50 characters should handle even the largest multi-boxing setups.
        /// </summary>
        public int MaxCachedCharacters { get; set; } = 50;

        /// <summary>
        /// Enables predictive caching based on usage patterns.
        /// When enabled, frequently accessed characters are kept in cache longer.
        /// Default: true for optimal gaming performance.
        /// </summary>
        public bool EnablePredictiveCaching { get; set; } = true;

        /// <summary>
        /// Enables comprehensive performance monitoring and metrics collection.
        /// When disabled, only basic success/failure tracking is performed.
        /// Default: true to help users optimize their setup.
        /// </summary>
        public bool EnablePerformanceMonitoring { get; set; } = true;

        /// <summary>
        /// Number of recent activation attempts to keep in memory for performance analysis.
        /// Higher values provide more detailed history but use more memory.
        /// Default: 1000 provides good balance between detail and memory usage.
        /// </summary>
        public int PerformanceHistorySize { get; set; } = 1000;
        
        /// <summary>
        /// Hotkey cooldown period in milliseconds to prevent rapid-fire activation spam.
        /// Minimum time between hotkey activations for the same character.
        /// Default: 100ms prevents resource contention while maintaining responsiveness.
        /// </summary>
        public int HotkeySpamCooldownMs { get; set; } = 100;
        
        /// <summary>
        /// Enable predictive caching for frequently accessed characters.
        /// When enabled, keeps recently used characters in cache longer.
        /// Default: true for improved gaming performance.
        /// </summary>
        public bool EnablePredictiveCharacterCaching { get; set; } = true;

        /// <summary>
        /// Automatically adjusts cache timing based on system performance.
        /// When enabled, cache validity periods are shortened on slow systems and extended on fast systems.
        /// Default: true for adaptive performance optimization.
        /// </summary>
        public bool EnableAdaptivePerformanceTuning { get; set; } = true;

        // Window state persistence
        public double MainWindowWidth { get; set; } = 1200; // Increased default width
        public double MainWindowHeight { get; set; } = 700; // Increased default height
        public double MainWindowLeft { get; set; } = double.NaN; // NaN = center on screen
        public double MainWindowTop { get; set; } = double.NaN; // NaN = center on screen
        public bool MainWindowMaximized { get; set; }
        public bool RememberWindowPosition { get; set; } = true;

        /// <summary>
        /// Gets the default keyboard shortcuts for character switching (Win+F1 through Win+F12)
        /// Uses Windows key to avoid conflicts with FFXI's Ctrl/Alt macro system
        /// </summary>
        public static List<KeyboardShortcutConfig> GetDefaultShortcuts()
        {
            var shortcuts = new List<KeyboardShortcutConfig>();
            for (int i = 0; i < 12; i++) // Extended to F12 for better peripheral support
            {
                var key = (Key)(Key.F1 + i); // F1, F2, F3... F12
                shortcuts.Add(new KeyboardShortcutConfig(i, ModifierKeys.Windows, key));
            }
            return shortcuts;
        }
    }
}


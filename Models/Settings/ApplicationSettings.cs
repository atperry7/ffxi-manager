
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

        // Window state persistence
        public double MainWindowWidth { get; set; } = 1200; // Increased default width
        public double MainWindowHeight { get; set; } = 700; // Increased default height
        public double MainWindowLeft { get; set; } = double.NaN; // NaN = center on screen
        public double MainWindowTop { get; set; } = double.NaN; // NaN = center on screen
        public bool MainWindowMaximized { get; set; }
        public bool RememberWindowPosition { get; set; } = true;

        /// <summary>
        /// Gets the default keyboard shortcuts for character switching (Win+F1 through Win+F9)
        /// Uses Windows key to avoid conflicts with FFXI's Ctrl/Alt macro system
        /// </summary>
        public static List<KeyboardShortcutConfig> GetDefaultShortcuts()
        {
            var shortcuts = new List<KeyboardShortcutConfig>();
            for (int i = 0; i < 9; i++)
            {
                var key = (Key)(Key.F1 + i); // F1, F2, F3... F9
                shortcuts.Add(new KeyboardShortcutConfig(i, ModifierKeys.Windows, key));
            }
            return shortcuts;
        }
    }
}


using System;
using System.Windows;
using FFXIManager.Infrastructure;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    /// <summary>
    /// Helper class to manage Character Monitor windows.
    /// Provides a migration path from the old implementation to the new one.
    /// </summary>
    public static class CharacterMonitorHelper
    {
        private static CharacterMonitorWindowV2? _currentWindow;
        private static readonly object _lock = new object();

        /// <summary>
        /// Shows the Character Monitor window using the new architecture.
        /// If a window is already open, it will be brought to the front.
        /// </summary>
        public static void ShowCharacterMonitor()
        {
            lock (_lock)
            {
                if (_currentWindow != null)
                {
                    // Window already exists, bring it to front
                    if (_currentWindow.WindowState == WindowState.Minimized)
                        _currentWindow.WindowState = WindowState.Normal;
                    
                    _currentWindow.Activate();
                    _currentWindow.Focus();
                    return;
                }

                // Create new window
                try
                {
                    _currentWindow = new CharacterMonitorWindowV2();
                    _currentWindow.Closed += OnWindowClosed;
                    _currentWindow.Show();
                    
                    var loggingService = ServiceLocator.LoggingService;
                    _ = loggingService.LogInfoAsync(
                        "Character Monitor window opened (new architecture)", 
                        "CharacterMonitorHelper");
                }
                catch (Exception ex)
                {
                    var loggingService = ServiceLocator.LoggingService;
                    _ = loggingService.LogErrorAsync(
                        "Failed to open Character Monitor window", 
                        ex, 
                        "CharacterMonitorHelper");
                    
                    MessageBox.Show(
                        $"Failed to open Character Monitor: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Shows the Character Monitor window (compatibility method for old code).
        /// This method exists to support the old PlayOnlineMonitorViewModel.
        /// </summary>
        /// <param name="legacyViewModel">The old view model (ignored)</param>
        public static void ShowCharacterMonitorLegacy(PlayOnlineMonitorViewModel? legacyViewModel = null)
        {
            // Ignore the legacy view model and use the new architecture
            ShowCharacterMonitor();
        }

        /// <summary>
        /// Closes the current Character Monitor window if one is open.
        /// </summary>
        public static void CloseCharacterMonitor()
        {
            lock (_lock)
            {
                if (_currentWindow != null)
                {
                    _currentWindow.Close();
                    _currentWindow = null;
                }
            }
        }

        /// <summary>
        /// Gets whether a Character Monitor window is currently open.
        /// </summary>
        public static bool IsCharacterMonitorOpen
        {
            get
            {
                lock (_lock)
                {
                    return _currentWindow != null;
                }
            }
        }

        private static void OnWindowClosed(object? sender, EventArgs e)
        {
            lock (_lock)
            {
                if (_currentWindow != null)
                {
                    _currentWindow.Closed -= OnWindowClosed;
                    _currentWindow = null;
                }
            }
        }
    }
}
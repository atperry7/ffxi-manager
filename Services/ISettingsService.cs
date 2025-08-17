using System.Threading.Tasks;
using FFXIManager.Models.Settings;

namespace FFXIManager.Services
{
    /// <summary>
    /// Simple interface for settings management
    /// </summary>
    public interface ISettingsService
    {
        ApplicationSettings LoadSettings();
        void SaveSettings(ApplicationSettings settings);

        /// <summary>
        /// Updates theme-related settings (IsDarkTheme, CharacterMonitorOpacity)
        /// </summary>
        /// <param name="isDarkTheme">Whether dark theme is enabled</param>
        /// <param name="characterMonitorOpacity">Character monitor window opacity (0.0 to 1.0)</param>
        void UpdateTheme(bool isDarkTheme, double characterMonitorOpacity);

        /// <summary>
        /// Updates window bounds and position settings
        /// </summary>
        /// <param name="width">Main window width</param>
        /// <param name="height">Main window height</param>
        /// <param name="left">Main window left position (use double.NaN to center)</param>
        /// <param name="top">Main window top position (use double.NaN to center)</param>
        /// <param name="isMaximized">Whether the window is maximized</param>
        /// <param name="rememberPosition">Whether to remember window position</param>
        void UpdateWindowBounds(double width, double height, double left, double top, bool isMaximized, bool rememberPosition);

        /// <summary>
        /// Updates diagnostics and logging settings
        /// </summary>
        /// <param name="enableDiagnostics">Master toggle for diagnostics</param>
        /// <param name="verboseLogging">Enable debug-level logging</param>
        /// <param name="maxLogEntries">Maximum number of log entries to keep</param>
        void UpdateDiagnostics(bool enableDiagnostics, bool verboseLogging, int maxLogEntries);
    }
}

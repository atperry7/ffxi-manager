using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Interface for multi-monitor positioning verification and correction.
    /// Ensures applications are positioned on the correct monitor for reliable automation.
    /// </summary>
    public interface IMonitorPositioningService
    {
        /// <summary>
        /// Verifies that an application is positioned on the primary monitor.
        /// If not on the primary monitor, attempts to reposition it automatically.
        /// </summary>
        /// <param name="processId">The process ID to verify</param>
        /// <param name="applicationName">The application name for logging purposes</param>
        /// <param name="subtask">The subtask for progress reporting</param>
        /// <returns>True if the application is on the primary monitor or was successfully repositioned, false otherwise</returns>
        Task<bool> VerifyAndCorrectMonitorPositionAsync(
            int processId,
            string applicationName,
            AutoLoginSubtask subtask);
    }
}

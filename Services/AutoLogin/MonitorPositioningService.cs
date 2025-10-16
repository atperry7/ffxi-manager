using System;
using System.Linq;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Infrastructure;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Implementation of multi-monitor positioning verification and correction.
    /// Ensures applications are positioned on the correct monitor for reliable automation.
    /// </summary>
    public class MonitorPositioningService : IMonitorPositioningService
    {
        private readonly ILoggingService _loggingService;
        private readonly IProcessUtilityService _processUtilityService;

        public MonitorPositioningService(
            ILoggingService loggingService,
            IProcessUtilityService processUtilityService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _processUtilityService = processUtilityService ?? throw new ArgumentNullException(nameof(processUtilityService));
        }

        public async Task<bool> VerifyAndCorrectMonitorPositionAsync(
            int processId,
            string applicationName,
            AutoLoginSubtask subtask)
        {
            try
            {
                await _loggingService.LogInfoAsync($"[MONITOR] Verifying {applicationName} (PID: {processId}) is positioned on primary monitor");

                // Wait briefly for window creation since process may have just started
                await Task.Delay(1000);

                var windows = await _processUtilityService.GetProcessWindowsAsync(processId);
                if (windows.Count == 0)
                {
                    await _loggingService.LogWarningAsync($"[MONITOR] No windows found for {applicationName} process {processId} - window may not be created yet");
                    return false;
                }

                // Find the main window
                var mainWindow = windows.FirstOrDefault(w => w.IsVisible && w.IsMainWindow) ?? windows.FirstOrDefault(w => w.IsVisible);
                if (mainWindow == null)
                {
                    await _loggingService.LogWarningAsync($"[MONITOR] No visible {applicationName} window found for process {processId}");
                    return false;
                }

                await _loggingService.LogInfoAsync($"[MONITOR] Found {applicationName} window: '{mainWindow.Title}' (Handle: 0x{mainWindow.Handle.ToInt64():X})");

                // Check monitor positioning
                bool isOnPrimaryMonitor = _processUtilityService.IsWindowOnPrimaryMonitor(mainWindow.Handle);

                if (isOnPrimaryMonitor)
                {
                    await _loggingService.LogInfoAsync($"[MONITOR] ✅ {applicationName} window is correctly positioned on primary monitor");

                    // Get additional monitor information for debugging
                    var primaryBounds = _processUtilityService.GetPrimaryMonitorBounds();
                    var windowBounds = _processUtilityService.GetWindowMonitorBounds(mainWindow.Handle);

                    await _loggingService.LogDebugAsync($"[MONITOR] Primary monitor bounds: {primaryBounds}");
                    await _loggingService.LogDebugAsync($"[MONITOR] Window monitor bounds: {windowBounds}");
                    return true;
                }
                else
                {
                    await _loggingService.LogWarningAsync($"[MONITOR] ⚠️ {applicationName} window is NOT on primary monitor - this may cause click coordinate issues");

                    // Get detailed positioning information for troubleshooting
                    var primaryBounds = _processUtilityService.GetPrimaryMonitorBounds();
                    var windowBounds = _processUtilityService.GetWindowMonitorBounds(mainWindow.Handle);

                    await _loggingService.LogWarningAsync($"[MONITOR] Primary monitor bounds: {primaryBounds}");
                    await _loggingService.LogWarningAsync($"[MONITOR] Window is on monitor with bounds: {windowBounds}");
                    await _loggingService.LogWarningAsync($"[MONITOR] Auto-login clicks may fail - consider manually moving {applicationName} to primary monitor");

                    // Attempt to reposition the window
                    await _loggingService.LogInfoAsync($"[MONITOR] Attempting to reposition {applicationName} window to primary monitor");
                    bool repositionSuccess = await _processUtilityService.MoveWindowToPrimaryMonitorAsync(mainWindow.Handle);

                    if (repositionSuccess)
                    {
                        await _loggingService.LogInfoAsync($"[MONITOR] ✅ Successfully repositioned {applicationName} to primary monitor");
                        return true;
                    }
                    else
                    {
                        await _loggingService.LogWarningAsync($"[MONITOR] ❌ Failed to reposition {applicationName} - manual positioning may be required");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"[MONITOR] Error verifying {applicationName} monitor position", ex);
                return false;
            }
        }
    }
}

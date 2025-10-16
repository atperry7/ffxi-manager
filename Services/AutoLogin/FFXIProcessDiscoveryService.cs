using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.Configuration;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.Infrastructure;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Implementation of FFXI process discovery and window handle management.
    /// Handles the complex transition from PlayOnline to FFXI process.
    /// </summary>
    public class FFXIProcessDiscoveryService : IFFXIProcessDiscoveryService
    {
        private readonly ILoggingService _loggingService;
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly IProcessUtilityService _processUtilityService;

        public FFXIProcessDiscoveryService(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            IProcessUtilityService processUtilityService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
            _processUtilityService = processUtilityService ?? throw new ArgumentNullException(nameof(processUtilityService));
        }

        public async Task<IntPtr> WaitForFFXIProcessAsync(
            AutoLoginSubtask subtask,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            var maxAttempts = FFXIGameConfiguration.ProcessDiscovery.ProcessDetectionAttempts;
            var attempt = 0;

            await _loggingService.LogInfoAsync("[PID_TRACKING] Waiting for PlayOnline to FFXI transition by monitoring window handle changes...");

            // Get stored PlayOnline context
            var playOnlinePidData = context.GetValueData<int>("PlayOnlineProcessId");
            var playOnlinePid = playOnlinePidData == 0 ? (int?)null : playOnlinePidData;
            var playOnlineHandle = context.GetValueData<IntPtr>("PlayOnlineWindowHandle");

            if (!playOnlinePid.HasValue || playOnlineHandle == IntPtr.Zero)
            {
                await _loggingService.LogWarningAsync("[PID_TRACKING] No PlayOnline PID/handle context found - falling back to process discovery");
                return await FallbackProcessDiscoveryAsync(subtask, cancellationToken);
            }

            await _loggingService.LogInfoAsync($"[PID_TRACKING] Monitoring PID {playOnlinePid} for window handle changes (current: 0x{playOnlineHandle.ToInt64():X})");

            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                // Use monotonic progress within transition monitoring range (10-40%)
                var baseProgress = 10;
                var rangeSize = 30;
                var progress = baseProgress + ((attempt - 1) * rangeSize / maxAttempts);
                await UpdateProgressWithPhaseAsync(subtask, "gameconnection", Math.Min(40, progress), "Monitoring game transition");

                await _loggingService.LogInfoAsync($"[PID_TRACKING] Attempt {attempt}/{maxAttempts} - Checking PID {playOnlinePid} for new windows...");

                // Use ProcessUtilityService to get windows for the specific PID
                var processWindows = await _processUtilityService.GetProcessWindowsAsync(playOnlinePid.Value);

                await _loggingService.LogInfoAsync($"[PID_TRACKING] Found {processWindows.Count} windows in PID {playOnlinePid}");

                foreach (var window in processWindows)
                {
                    await _loggingService.LogInfoAsync($"[PID_TRACKING] - Window: 0x{window.Handle.ToInt64():X}, Title: '{window.Title}'");

                    // Look for a different window handle with FFXI title patterns
                    if (window.Handle != playOnlineHandle && window.Handle != IntPtr.Zero && !string.IsNullOrEmpty(window.Title))
                    {
                        // Check if this window has an FFXI title
                        await _loggingService.LogInfoAsync($"[PID_TRACKING] New window detected - Testing title: '{window.Title}' against FFXI patterns");

                        bool isFFXITitle = false;
                        foreach (var pattern in FFXIGameConfiguration.ProcessDiscovery.WindowTitlePatterns)
                        {
                            if (window.Title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                            {
                                await _loggingService.LogInfoAsync($"[PID_TRACKING] ✓ FFXI pattern match: '{pattern}' in '{window.Title}'");
                                isFFXITitle = true;
                                break;
                            }
                        }

                        if (isFFXITitle)
                        {
                            await _loggingService.LogInfoAsync($"[PID_TRACKING] ✅ SUCCESS - FFXI transition detected! PID {playOnlinePid}, Handle: 0x{playOnlineHandle.ToInt64():X} → 0x{window.Handle.ToInt64():X}");
                            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 45, "FFXI window transition detected");
                            return window.Handle;
                        }
                        else
                        {
                            await _loggingService.LogInfoAsync($"[PID_TRACKING] New window 0x{window.Handle.ToInt64():X} does not match FFXI patterns: '{window.Title}'");
                        }
                    }
                }

                // No transition detected yet, wait before next attempt
                if (attempt < maxAttempts)
                {
                    await _loggingService.LogInfoAsync($"[PID_TRACKING] No FFXI window transition detected, waiting {FFXIGameConfiguration.PollingIntervals.ProcessCheck.TotalSeconds}s...");
                    await Task.Delay(FFXIGameConfiguration.PollingIntervals.ProcessCheck, cancellationToken);
                }
            }

            await _loggingService.LogWarningAsync($"[PID_TRACKING] No FFXI transition detected after {maxAttempts} attempts - falling back to process discovery");
            return await FallbackProcessDiscoveryAsync(subtask, cancellationToken);
        }

        public async Task WaitForFFXIStartupAsync(
            IntPtr windowHandle,
            AutoLoginSubtask subtask,
            CancellationToken cancellationToken)
        {
            var maxAttempts = FFXIGameConfiguration.ProcessDiscovery.ResponsivenessCheckAttempts;
            var attempt = 0;

            await _loggingService.LogInfoAsync("Waiting for Final Fantasy XI window to become responsive...");

            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Use monotonic progress within responsiveness range (50-65%)
                var baseProgress = 50;
                var rangeSize = 15;
                var progress = baseProgress + (attempt * rangeSize / maxAttempts);
                await UpdateProgressWithPhaseAsync(subtask, "gameconnection", Math.Min(65, progress), "Checking FFXI responsiveness");

                try
                {
                    // Capture window and check if it's responsive
                    var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);

                    if (screenshot != null && screenshot.IsValid && screenshot.Width > 0 && screenshot.Height > 0)
                    {
                        var windowTitle = screenshot.WindowTitle;
                        await _loggingService.LogDebugAsync($"FFXI startup check {attempt + 1}/{maxAttempts}: Window responsive, title: '{windowTitle}', size: {screenshot.Width}x{screenshot.Height}");

                        // FFXI-Specific: Multiple successful screenshots required to ensure DirectX stability
                        // Single successful capture may occur during initialization but window may still be unstable
                        if (attempt >= 2)
                        {
                            await _loggingService.LogInfoAsync("Final Fantasy XI window initialization completed");
                            break; // Window is consistently responsive - safe to proceed
                        }
                    }
                    else
                    {
                        await _loggingService.LogDebugAsync($"FFXI startup check {attempt + 1}/{maxAttempts}: Invalid screenshot");
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogDebugAsync($"FFXI startup check {attempt + 1}/{maxAttempts}: Exception during capture: {ex.Message}");
                }

                attempt++;
                await Task.Delay(FFXIGameConfiguration.PollingIntervals.ProcessCheck, cancellationToken);
            }

            // FFXI-Specific: DirectX rendering pipeline requires additional stabilization
            // This final delay ensures the window is fully ready for template matching and UI automation
            await Task.Delay(FFXIGameConfiguration.Delays.ScreenStabilization, cancellationToken);
        }

        public async Task<IntPtr> GetFFXIWindowHandleAsync(
            AutoLoginSubtask subtask,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // Try to get FFXI-specific handle first
            var ffxiHandle = context.GetValueData<IntPtr>("FFXIWindowHandle");
            if (ffxiHandle != IntPtr.Zero)
            {
                try
                {
                    // Verify it's still valid
                    var screenshot = await _screenshotService.CaptureWindowAsync(ffxiHandle, cancellationToken);
                    if (screenshot != null && screenshot.IsValid)
                    {
                        await _loggingService.LogDebugAsync($"Using existing FFXI window handle: 0x{ffxiHandle.ToInt64():X}");
                        return ffxiHandle;
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogDebugAsync($"Existing FFXI handle invalid: {ex.Message}");
                }
            }

            // If not found or invalid, find it again
            await _loggingService.LogInfoAsync("FFXI window handle not found in context, searching for process...");
            ffxiHandle = await WaitForFFXIProcessAsync(subtask, context, cancellationToken);
            context.SetData("FFXIWindowHandle", ffxiHandle);
            return ffxiHandle;
        }

        /// <summary>
        /// Fallback method for FFXI process detection when PID tracking fails.
        /// Uses traditional process enumeration and window title matching.
        /// </summary>
        private async Task<IntPtr> FallbackProcessDiscoveryAsync(
            AutoLoginSubtask subtask,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync("[FALLBACK] Using traditional FFXI process discovery...");

            var maxAttempts = FFXIGameConfiguration.ProcessDiscovery.ProcessDetectionAttempts;
            var attempt = 0;

            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                await _loggingService.LogInfoAsync($"[FALLBACK] Attempt {attempt}/{maxAttempts} - Scanning for FFXI processes...");

                var ffxiHandle = await CreateFallbackWindowSearchAsync(cancellationToken);
                if (ffxiHandle != IntPtr.Zero)
                {
                    await _loggingService.LogInfoAsync($"[FALLBACK] ✅ Found FFXI window: 0x{ffxiHandle.ToInt64():X}");
                    return ffxiHandle;
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(FFXIGameConfiguration.PollingIntervals.ProcessCheck, cancellationToken);
                }
            }

            throw new InvalidOperationException($"FFXI process could not be detected using fallback method after {maxAttempts} attempts");
        }

        /// <summary>
        /// Fallback method using ProcessUtilityService to search for FFXI windows by title patterns.
        /// Leverages the existing infrastructure instead of duplicating process enumeration logic.
        /// </summary>
        private async Task<IntPtr> CreateFallbackWindowSearchAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Use ProcessUtilityService to get processes with their windows
                var processNames = new[] { "pol", "ffxi", "ffximain", "PlayOnlineViewer" };
                var processes = await _processUtilityService.GetProcessesByNamesAsync(processNames);

                await _loggingService.LogInfoAsync($"[FALLBACK] Found {processes.Count} processes across {processNames.Length} process names");

                foreach (var process in processes)
                {
                    await _loggingService.LogDebugAsync($"[FALLBACK] Checking process '{process.ProcessName}' (PID: {process.ProcessId}) with {process.Windows.Count} windows");

                    foreach (var window in process.Windows)
                    {
                        if (window.Handle != IntPtr.Zero && !string.IsNullOrEmpty(window.Title))
                        {
                            // Check if this window title matches FFXI patterns
                            foreach (var pattern in FFXIGameConfiguration.ProcessDiscovery.WindowTitlePatterns)
                            {
                                if (window.Title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                {
                                    await _loggingService.LogInfoAsync($"[FALLBACK] ✅ Found FFXI window: '{window.Title}' in process '{process.ProcessName}' (PID: {process.ProcessId}, Handle: 0x{window.Handle.ToInt64():X})");
                                    return window.Handle;
                                }
                            }
                        }
                    }
                }

                await _loggingService.LogDebugAsync("[FALLBACK] No FFXI windows found in fallback search");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("[FALLBACK] Error in fallback window search", ex);
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Updates subtask progress with phase information.
        /// </summary>
        private async Task UpdateProgressWithPhaseAsync(
            AutoLoginSubtask subtask,
            string phase,
            int progressValue,
            string message)
        {
            subtask.UpdateProgress(progressValue, message);
            await Task.CompletedTask;
        }
    }
}

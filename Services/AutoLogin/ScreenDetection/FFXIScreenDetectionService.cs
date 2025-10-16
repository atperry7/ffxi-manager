using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.Configuration;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.Infrastructure;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Implementation of FFXI-specific screen detection with automatic window handle redetection.
    /// Extracts and centralizes the screen detection patterns used throughout FFXIGameHandler.
    /// </summary>
    public class FFXIScreenDetectionService : IFFXIScreenDetectionService
    {
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly ITemplateMatchingService _templateService;
        private readonly ITemplateManagementService _templateManagementService;
        private readonly ILoggingService _loggingService;
        private readonly IProcessUtilityService _processUtilityService;

        public FFXIScreenDetectionService(
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            ILoggingService loggingService,
            IProcessUtilityService processUtilityService)
        {
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _templateManagementService = templateManagementService ?? throw new ArgumentNullException(nameof(templateManagementService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _processUtilityService = processUtilityService ?? throw new ArgumentNullException(nameof(processUtilityService));
        }

        public async Task<TemplateMatchResult> WaitForScreenWithRedetectionFallbackAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr windowHandle,
            IAutoLoginContext context,
            string screenDescription,
            TimeSpan timeout,
            TimeSpan checkInterval,
            CancellationToken cancellationToken)
        {
            try
            {
                // Try standard detection first (uses base class WaitForScreenDetectionAsync pattern)
                return await WaitForScreenDetectionAsync(
                    subtask,
                    templatePath,
                    windowHandle,
                    screenDescription,
                    cancellationToken,
                    new ScreenDetectionOptions
                    {
                        Timeout = timeout,
                        CheckInterval = checkInterval
                    });
            }
            catch (TimeoutException)
            {
                // Fallback to window redetection if standard detection fails
                await _loggingService.LogInfoAsync($"Standard {screenDescription} detection failed, attempting with window redetection");
                return await WaitForScreenDetectionWithRedetectionAsync(
                    subtask,
                    templatePath,
                    windowHandle,
                    context,
                    screenDescription,
                    cancellationToken,
                    new ScreenDetectionOptions
                    {
                        Timeout = timeout,
                        CheckInterval = checkInterval
                    });
            }
        }

        public async Task<TemplateMatchResult> WaitForScreenDetectionWithRedetectionAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr initialWindowHandle,
            IAutoLoginContext context,
            string screenDescription,
            CancellationToken cancellationToken,
            ScreenDetectionOptions? options = null)
        {
            options ??= ScreenDetectionOptions.Default;

            // Load template metadata to get the configured confidence threshold
            var templateMetadata = await _templateManagementService.GetTemplateMetadataAsync(templatePath);
            if (templateMetadata == null)
            {
                throw new InvalidOperationException($"Template metadata not found for: {templatePath}");
            }

            var confidenceThreshold = templateMetadata.ConfidenceThreshold;
            await _loggingService.LogInfoAsync($"Using template confidence threshold: {confidenceThreshold:P} for {screenDescription}");
            await _loggingService.LogInfoAsync($"Waiting for {screenDescription} (max {options.Timeout.TotalSeconds}s, checking every {options.CheckInterval.TotalSeconds}s, confidence={confidenceThreshold:P})");

            // Orchestrate the detection process using focused helper methods
            var detectionResult = await DetectScreenWithRetryAsync(
                subtask,
                templatePath,
                initialWindowHandle,
                context,
                screenDescription,
                options,
                confidenceThreshold,
                cancellationToken);

            if (detectionResult != null)
            {
                await _loggingService.LogInfoAsync($"{screenDescription} detected successfully (confidence: {detectionResult.Confidence:P})");
                return detectionResult;
            }

            // Final diagnostic attempt if detection failed
            await _loggingService.LogWarningAsync($"{screenDescription} detection failed after timeout");
            try
            {
                var currentHandle = context.GetValueData<IntPtr>("FFXIWindowHandle");
                if (currentHandle == IntPtr.Zero) currentHandle = initialWindowHandle;

                var finalScreenshot = await _screenshotService.CaptureWindowAsync(currentHandle, cancellationToken);
                var finalMatch = await _templateService.FindElementAsync(finalScreenshot!, templatePath, cancellationToken);

                await _loggingService.LogWarningAsync($"{screenDescription} final diagnostic attempt - confidence: {finalMatch.Confidence:P}");
                return finalMatch;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Final diagnostic attempt failed for {screenDescription}", ex);
                return new TemplateMatchResult { Confidence = 0.0f };
            }
        }

        /// <summary>
        /// Standard screen detection using base detection pattern (mimics BaseLoginTaskHandler.WaitForScreenDetectionAsync).
        /// This is a simplified version without redetection logic.
        /// </summary>
        private async Task<TemplateMatchResult> WaitForScreenDetectionAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr windowHandle,
            string screenDescription,
            CancellationToken cancellationToken,
            ScreenDetectionOptions options)
        {
            var templateMetadata = await _templateManagementService.GetTemplateMetadataAsync(templatePath);
            if (templateMetadata == null)
            {
                throw new InvalidOperationException($"Template metadata not found for: {templatePath}");
            }

            var confidenceThreshold = templateMetadata.ConfidenceThreshold;
            var maxAttempts = (int)(options.Timeout.TotalSeconds / options.CheckInterval.TotalSeconds);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);
                    if (screenshot != null && screenshot.IsValid)
                    {
                        var match = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);
                        if (match.Confidence >= confidenceThreshold)
                        {
                            return match;
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogDebugAsync($"Standard detection attempt {attempt} failed: {ex.Message}");
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(options.CheckInterval, cancellationToken);
                }
            }

            throw new TimeoutException($"{screenDescription} not detected within {options.Timeout.TotalSeconds}s");
        }

        /// <summary>
        /// Performs core screen detection with retry logic and window handle redetection.
        /// Centralizes the main detection loop with proper error handling and progress reporting.
        /// </summary>
        private async Task<TemplateMatchResult?> DetectScreenWithRetryAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr initialWindowHandle,
            IAutoLoginContext context,
            string screenDescription,
            ScreenDetectionOptions options,
            float confidenceThreshold,
            CancellationToken cancellationToken)
        {
            var currentWindowHandle = initialWindowHandle;
            var maxAttempts = (int)(options.Timeout.TotalSeconds / options.CheckInterval.TotalSeconds);
            var consecutiveFailures = 0;
            var maxConsecutiveFailures = FFXIGameConfiguration.ProcessDiscovery.MaxConsecutiveFailures;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Update progress - use monotonic progress within the detection range (20-80%)
                var baseProgress = 20; // Start of detection range
                var rangeSize = 60; // Detection range size (80% - 20%)
                var progress = baseProgress + ((attempt - 1) * rangeSize / maxAttempts);
                await UpdateProgressWithPhaseAsync(subtask, "gameconnection", Math.Min(80, progress), $"Detecting {screenDescription}");

                try
                {
                    var screenshot = await _screenshotService.CaptureWindowAsync(currentWindowHandle, cancellationToken);

                    if (screenshot == null || !screenshot.IsValid)
                    {
                        consecutiveFailures++;
                        await _loggingService.LogDebugAsync($"{screenDescription} screenshot failed (attempt {attempt}/{maxAttempts}) - consecutive failures: {consecutiveFailures}");

                        // FFXI-Specific: Window handles can become invalid during screen transitions
                        // This is common when FFXI changes resolution or enters fullscreen mode
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            var newHandle = await HandleWindowRedetectionAsync(currentWindowHandle, context, consecutiveFailures, cancellationToken);
                            if (newHandle != currentWindowHandle)
                            {
                                currentWindowHandle = newHandle;
                                consecutiveFailures = 0;
                                continue; // Immediate retry with fresh window handle - no delay needed
                            }
                        }
                    }
                    else
                    {
                        // Screenshot successful, reset failure count and try template matching
                        consecutiveFailures = 0;
                        var match = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);

                        await _loggingService.LogDebugAsync($"{screenDescription} detection attempt {attempt}/{maxAttempts}: confidence={match.Confidence:P}, threshold={confidenceThreshold:P}");

                        if (await ValidateDetectionResultAsync(match, confidenceThreshold, screenDescription, cancellationToken))
                        {
                            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 85, $"{screenDescription} detected successfully");
                            context.SetData("FFXIWindowHandle", currentWindowHandle);
                            return match;
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogDebugAsync($"Exception during {screenDescription} detection attempt {attempt}: {ex.Message}");
                    consecutiveFailures++;
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(options.CheckInterval, cancellationToken);
                }
            }

            return null; // Detection failed
        }

        /// <summary>
        /// Validates and logs template matching results with appropriate diagnostic information.
        /// Provides detailed confidence analysis to aid in troubleshooting detection issues.
        /// </summary>
        private async Task<bool> ValidateDetectionResultAsync(
            TemplateMatchResult match,
            float confidenceThreshold,
            string screenDescription,
            CancellationToken cancellationToken)
        {
            if (match.Confidence >= confidenceThreshold)
            {
                await _loggingService.LogInfoAsync($"{screenDescription} detection successful (confidence: {match.Confidence:P})");
                return true;
            }

            // Enhanced diagnostic logging for different confidence ranges
            if (match.Confidence < 0.10f)
            {
                await _loggingService.LogWarningAsync($"{screenDescription} very low confidence ({match.Confidence:P}) - possible template mismatch or screen state issue");
            }
            else if (match.Confidence >= 0.60f)
            {
                await _loggingService.LogDebugAsync($"{screenDescription} partially detected (confidence: {match.Confidence:P}) - getting close");
            }
            else if (match.Confidence >= 0.30f)
            {
                await _loggingService.LogDebugAsync($"{screenDescription} moderate confidence ({match.Confidence:P}) - template may be partially visible");
            }

            return false;
        }

        /// <summary>
        /// Handles window handle redetection when consecutive screenshot failures occur.
        /// Updates the context with the new window handle if redetection is successful.
        /// </summary>
        private async Task<IntPtr> HandleWindowRedetectionAsync(
            IntPtr currentWindowHandle,
            IAutoLoginContext context,
            int consecutiveFailures,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"Re-detecting FFXI window handle after {consecutiveFailures} consecutive screenshot failures");

            try
            {
                // Get the PID of the current (possibly stale) window for prioritization
                int? currentPid = null;
                if (currentWindowHandle != IntPtr.Zero)
                {
                    try
                    {
                        var currentScreenshot = await _screenshotService.CaptureWindowAsync(currentWindowHandle, cancellationToken);
                        currentPid = currentScreenshot?.ProcessId;
                        await _loggingService.LogDebugAsync($"[REDETECTION] Current window handle 0x{currentWindowHandle.ToInt64():X} belongs to PID: {currentPid}");
                    }
                    catch
                    {
                        await _loggingService.LogDebugAsync($"[REDETECTION] Could not determine PID for current handle 0x{currentWindowHandle.ToInt64():X}");
                    }
                }

                // Use process utility service for basic window enumeration
                await _loggingService.LogDebugAsync("[REDETECTION] Using fallback process discovery...");
                var ffxiHandle = await CreateFallbackWindowSearchAsync(cancellationToken);
                if (ffxiHandle != IntPtr.Zero)
                {
                    await _loggingService.LogInfoAsync($"[REDETECTION] ✓ Found FFXI window: 0x{ffxiHandle.ToInt64():X}");
                    context.SetData("FFXIWindowHandle", ffxiHandle);
                    return ffxiHandle;
                }

                // No suitable windows found
                await _loggingService.LogWarningAsync("[REDETECTION] No valid FFXI windows found");
                return currentWindowHandle;
            }
            catch (Exception redetectEx)
            {
                await _loggingService.LogWarningAsync($"Failed to re-detect FFXI window: {redetectEx.Message}");
            }

            return currentWindowHandle;
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
        /// Updates subtask progress with phase information (mimics BaseLoginTaskHandler pattern).
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

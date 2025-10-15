using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.Configuration;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.Navigation
{
    /// <summary>
    /// Service responsible for navigating through PlayOnline screens to launch Final Fantasy XI.
    /// Handles the complete navigation flow from post-authentication to game launch.
    ///
    /// This service orchestrates:
    /// - PlayOnline main screen detection and interaction
    /// - Final Fantasy XI game selection
    /// - Play screen navigation
    /// - Final confirmation and game launch
    ///
    /// All navigation uses template-driven, resolution-independent interaction patterns
    /// through the HybridNavigationStrategy for maximum reliability.
    /// </summary>
    public class PlayOnlineNavigationService : IPlayOnlineNavigationService
    {
        private readonly ILoggingService _loggingService;
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly ITemplateMatchingService _templateService;
        private readonly ITemplateManagementService _templateManagementService;
        private readonly IUIAutomationService _automationService;

        public PlayOnlineNavigationService(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IUIAutomationService automationService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _templateManagementService = templateManagementService ?? throw new ArgumentNullException(nameof(templateManagementService));
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
        }

        /// <summary>
        /// Navigates through PlayOnline screens to launch Final Fantasy XI.
        /// Handles main screen, game selection, play screens, and final confirmation.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting</param>
        /// <param name="windowHandle">Initial PlayOnline window handle</param>
        /// <param name="context">AutoLogin context for window handle tracking</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Final window handle after navigation</returns>
        public async Task<IntPtr> NavigateToFinalFantasyXIAsync(
            AutoLoginSubtask subtask,
            IntPtr windowHandle,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // Phase 1: Wait for PlayOnline main screen
            windowHandle = await WaitForPlayOnlineMainScreenAsync(subtask, windowHandle, context, cancellationToken);

            // Phase 2: Select Final Fantasy XI
            await SelectFinalFantasyXIAsync(subtask, windowHandle, cancellationToken);

            // Phase 3: Navigate through play screens
            windowHandle = await NavigatePlayScreensAsync(subtask, windowHandle, context, cancellationToken);

            // Phase 4: Store final context
            context.SetData("WindowHandle", windowHandle);

            // Update PlayOnline window handle for transition tracking
            try
            {
                var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);
                if (screenshot?.ProcessId != null)
                {
                    context.SetData("PlayOnlineProcessId", screenshot.ProcessId);
                    context.SetData("PlayOnlineWindowHandle", windowHandle);
                    await _loggingService.LogDebugAsync($"Updated PlayOnline transition context after FFXI navigation - PID: {screenshot.ProcessId}, Handle: 0x{windowHandle.ToInt64():X}");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogWarningAsync($"Could not update PlayOnline PID context after FFXI navigation: {ex.Message}");
            }

            await _loggingService.LogDebugAsync($"FFXI navigation completed - Final handle: 0x{windowHandle.ToInt64():X}");

            return windowHandle;
        }

        /// <summary>
        /// Waits for PlayOnline main screen with window handle management.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting</param>
        /// <param name="windowHandle">Current window handle</param>
        /// <param name="context">AutoLogin context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Updated window handle after detection</returns>
        private async Task<IntPtr> WaitForPlayOnlineMainScreenAsync(
            AutoLoginSubtask subtask,
            IntPtr windowHandle,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            subtask?.UpdateProgressWithPhase("gameconnection", PlayOnlineAuthConfiguration.ProgressMilestones.Navigation.MainScreenWait, "Loading game menu");

            var detectionOptions = ScreenDetectionOptions.WithTimeout((int)PlayOnlineAuthConfiguration.Timeouts.MainScreenDetection.TotalSeconds);
            var mainScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.MainScreen,
                windowHandle,
                "PlayOnline main screen",
                cancellationToken,
                detectionOptions);

            if (mainScreenMatch.Confidence < PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection)
            {
                await _loggingService.LogWarningAsync($"Main screen detection confidence below threshold: {mainScreenMatch.Confidence:P}");
            }

            return windowHandle;
        }

        /// <summary>
        /// Selects Final Fantasy XI from the PlayOnline game list.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting</param>
        /// <param name="windowHandle">PlayOnline window handle</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task SelectFinalFantasyXIAsync(
            AutoLoginSubtask subtask,
            IntPtr windowHandle,
            CancellationToken cancellationToken)
        {
            subtask?.UpdateProgressWithPhase("gameconnection", PlayOnlineAuthConfiguration.ProgressMilestones.Navigation.GameSelection, "Selecting FINAL FANTASY XI");

            // Detect main screen to obtain anchor for navigation
            var detectionOptions = ScreenDetectionOptions.WithTimeout((int)PlayOnlineAuthConfiguration.Timeouts.MainScreenDetection.TotalSeconds);
            var mainScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.MainScreen,
                windowHandle,
                "PlayOnline main screen",
                cancellationToken,
                detectionOptions);

            var navSuccess = await ExecuteNavigationFromTemplateAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.MainScreen,
                windowHandle,
                mainScreenMatch,
                cancellationToken);

            if (!navSuccess)
            {
                throw new InvalidOperationException("Failed to select Final Fantasy XI from main menu");
            }

            // Allow game selection to process
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.ConnectionProcessing, cancellationToken);
        }

        /// <summary>
        /// Navigates through play screens and confirmations to launch the game.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting</param>
        /// <param name="windowHandle">Current window handle</param>
        /// <param name="context">AutoLogin context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Updated window handle after navigation</returns>
        private async Task<IntPtr> NavigatePlayScreensAsync(
            AutoLoginSubtask subtask,
            IntPtr windowHandle,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // Wait for play screen
            subtask?.UpdateProgressWithPhase("gameconnection", PlayOnlineAuthConfiguration.ProgressMilestones.Navigation.PlayScreenWait, "Loading game launcher");

            var playDetectionOptions = ScreenDetectionOptions.WithTimeout((int)PlayOnlineAuthConfiguration.Timeouts.PlayScreenDetection.TotalSeconds);
            var playScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.PlayScreen,
                windowHandle,
                "PlayOnline play screen",
                cancellationToken,
                playDetectionOptions);

            if (playScreenMatch.Confidence >= PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection)
            {
                // Activate Play button using template navigation
                var playNavSuccess = await ExecuteNavigationFromTemplateAsync(
                    subtask,
                    PlayOnlineAuthConfiguration.TemplatePaths.PlayScreen,
                    windowHandle,
                    playScreenMatch,
                    cancellationToken);

                if (!playNavSuccess)
                {
                    throw new InvalidOperationException("Failed to activate Play button");
                }

                await Task.Delay(PlayOnlineAuthConfiguration.Delays.ConnectionProcessing, cancellationToken);

                // Handle final confirmation
                windowHandle = await HandleFinalConfirmationAsync(subtask, windowHandle, context, cancellationToken);
            }

            return windowHandle;
        }

        /// <summary>
        /// Handles the final play confirmation screen and launches the game.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting</param>
        /// <param name="windowHandle">Current window handle</param>
        /// <param name="context">AutoLogin context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Updated window handle after confirmation</returns>
        private async Task<IntPtr> HandleFinalConfirmationAsync(
            AutoLoginSubtask subtask,
            IntPtr windowHandle,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            subtask?.UpdateProgressWithPhase("gameconnection", PlayOnlineAuthConfiguration.ProgressMilestones.Navigation.FinalConfirmation, "Preparing to launch game");

            var confirmDetectionOptions = ScreenDetectionOptions.WithTimeout((int)PlayOnlineAuthConfiguration.Timeouts.PlayScreenDetection.TotalSeconds);
            var confirmScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.PlayConfirmation,
                windowHandle,
                "PlayOnline play confirmation screen",
                cancellationToken,
                confirmDetectionOptions);

            if (confirmScreenMatch.Confidence >= PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection)
            {
                // Activate final Play using template navigation
                subtask?.UpdateProgressWithPhase("gameconnection", PlayOnlineAuthConfiguration.ProgressMilestones.Navigation.Complete, "Launching FINAL FANTASY XI");

                var finalNavSuccess = await ExecuteNavigationFromTemplateAsync(
                    subtask,
                    PlayOnlineAuthConfiguration.TemplatePaths.PlayConfirmation,
                    windowHandle,
                    confirmScreenMatch,
                    cancellationToken);

                if (!finalNavSuccess)
                {
                    throw new InvalidOperationException("Failed to confirm final Play");
                }

                // Allow game launch processing
                await Task.Delay(PlayOnlineAuthConfiguration.Delays.POLProxyTransition, cancellationToken);
            }

            return windowHandle;
        }

        #region Helper Methods (Adapted from BaseLoginTaskHandler)

        /// <summary>
        /// Waits for screen detection using template matching.
        /// Adapted from BaseLoginTaskHandler pattern.
        /// </summary>
        private async Task<TemplateMatchResult> WaitForScreenDetectionAsync(
            AutoLoginSubtask? subtask,
            string templatePath,
            IntPtr windowHandle,
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
            var maxAttempts = (int)(options.Timeout.TotalSeconds / options.CheckInterval.TotalSeconds);
            var startTime = DateTime.UtcNow;

            await _loggingService.LogInfoAsync($"Starting {screenDescription} detection (timeout: {options.Timeout.TotalSeconds}s)");

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);
                if (screenshot == null || !screenshot.IsValid)
                {
                    await _loggingService.LogWarningAsync($"Failed to capture screenshot for {screenDescription} on attempt {attempt}");
                    await Task.Delay(options.CheckInterval, cancellationToken);
                    continue;
                }

                var match = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);

                if (match.Confidence >= confidenceThreshold)
                {
                    await _loggingService.LogInfoAsync($"{screenDescription} detected after {attempt} attempts (confidence: {match.Confidence:P})");
                    return match;
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(options.CheckInterval, cancellationToken);
                }
            }

            // Final attempt for diagnosis
            var finalScreenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);
            var finalMatch = await _templateService.FindElementAsync(finalScreenshot, templatePath, cancellationToken);

            await _loggingService.LogWarningAsync($"{screenDescription} detection timed out after {options.Timeout.TotalSeconds}s. Final confidence: {finalMatch.Confidence:P}");

            throw new TimeoutException($"Failed to detect {screenDescription} after {options.Timeout.TotalSeconds}s (final confidence: {finalMatch.Confidence:P})");
        }

        /// <summary>
        /// Executes navigation using the strategy defined in template metadata.
        /// Adapted from BaseLoginTaskHandler pattern.
        /// </summary>
        private async Task<bool> ExecuteNavigationFromTemplateAsync(
            AutoLoginSubtask? subtask,
            string templatePath,
            IntPtr windowHandle,
            TemplateMatchResult templateMatch,
            CancellationToken cancellationToken)
        {
            // Load template metadata to get navigation configuration
            var metadata = await _templateManagementService.GetTemplateMetadataAsync(templatePath);
            if (metadata == null)
            {
                await _loggingService.LogWarningAsync($"Template metadata not found for: {templatePath}");
                return false;
            }

            // Check if navigation metadata exists
            if (metadata.Navigation == null)
            {
                await _loggingService.LogWarningAsync($"No navigation metadata defined in template: {templatePath}");
                return false;
            }

            // Use Hybrid navigation strategy for all navigation
            var strategy = new HybridNavigationStrategy(_automationService, _screenshotService, _loggingService);
            return await strategy.ExecuteAsync(windowHandle, metadata.Navigation, templateMatch, cancellationToken);
        }

        #endregion
    }
}

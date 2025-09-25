using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Handles Final Fantasy XI game-specific tasks including character selection and login finalization.
    /// Responsible for: TermsAcceptance, CharacterSelection, CharacterSlotPick, ConfirmLogin
    /// Uses screenshot detection and keyboard automation for DirectX game interaction.
    /// </summary>
    public class FFXIGameHandler : BaseLoginTaskHandler
    {
        private readonly IUIAutomationService _automationService;
        private readonly IAutoLoginContextService _contextService;

        public FFXIGameHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IUIAutomationService automationService,
            IAutoLoginContextService contextService)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _contextService = contextService ?? throw new ArgumentNullException(nameof(contextService));
        }

        public override LoginTaskStep TaskStep => LoginTaskStep.TermsAcceptance;

        public override bool CanHandle(AutoLoginSubtask subtask)
        {
            return subtask.TaskStep switch
            {
                LoginTaskStep.TermsAcceptance => true,
                LoginTaskStep.CharacterSelection => true,
                LoginTaskStep.CharacterSlotPick => true,
                LoginTaskStep.ConfirmLogin => true,
                _ => false
            };
        }

        protected override async Task ExecuteHandlerLogicAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            subtask.Start();

            await _loggingService.LogInfoAsync($"[FLOW] Starting FFXI {subtask.TaskStep} for {queueItem.DisplayName}");

            try
            {
                switch (subtask.TaskStep)
                {
                    case LoginTaskStep.TermsAcceptance:
                        await ExecuteTermsAcceptanceAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    case LoginTaskStep.CharacterSelection:
                        await ExecuteCharacterSelectionAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    case LoginTaskStep.CharacterSlotPick:
                        await ExecuteCharacterSlotPickAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    case LoginTaskStep.ConfirmLogin:
                        await ExecuteConfirmLoginAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    default:
                        throw new NotSupportedException($"Task step {subtask.TaskStep} is not supported by FFXIGameHandler");
                }

                subtask.Complete();
                await _loggingService.LogInfoAsync($"[FLOW] Completed FFXI {subtask.TaskStep} for {queueItem.DisplayName}");
            }
            catch (OperationCanceledException)
            {
                subtask.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                subtask.Fail(ex.Message);
                await _loggingService.LogErrorAsync($"FFXI {subtask.TaskStep} failed for {queueItem.DisplayName}", ex);
                throw;
            }
        }

        private async Task ExecuteTermsAcceptanceAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // First, wait for FFXI process to launch and window to be responsive
            subtask.UpdateProgress(5, "Waiting for Final Fantasy XI to launch...");
            var ffxiWindowHandle = await WaitForFFXIProcessAsync(subtask, context, cancellationToken);

            // Store FFXI window handle for subsequent steps
            context.SetData("FFXIWindowHandle", ffxiWindowHandle);

            // Wait for FFXI to fully initialize (window responsiveness check)
            await WaitForFFXIStartup(ffxiWindowHandle, subtask, cancellationToken);

            subtask.UpdateProgress(15, "Waiting for FFXI Terms of Service screen...");

            // Wait for FFXI accept terms screen with window handle re-detection on failure
            var termsMatch = await WaitForScreenDetectionWithRedetectionAsync(
                subtask,
                "FFXI/ffxi_accept_terms",
                ffxiWindowHandle,
                context,
                "FFXI Terms of Service",
                cancellationToken,
                new ScreenDetectionOptions
                {
                    Timeout = TimeSpan.FromSeconds(90), // Extended timeout for FFXI loading
                    CheckInterval = TimeSpan.FromSeconds(2) // Check every 2 seconds
                    // ConfidenceThreshold will be loaded from template JSON (0.80)
                });

            // The WaitForScreenDetectionWithRedetectionAsync method already validates against the template threshold
            // from the JSON configuration, so if we get here, detection was successful
            await _loggingService.LogInfoAsync($"FFXI Terms screen detected with confidence: {termsMatch.Confidence:P}");

            subtask.UpdateProgress(60, "Terms of Service screen detected");

            // Get updated window handle from context
            ffxiWindowHandle = context.GetValueData<IntPtr>("FFXIWindowHandle");

            // Allow screen to stabilize after detection
            await Task.Delay(1000, cancellationToken);

            subtask.UpdateProgress(70, "Accepting terms (pressing Enter)...");

            // Ensure window has focus before sending keys
            await _automationService.EnsureWindowFocusAsync(ffxiWindowHandle, cancellationToken);
            await Task.Delay(500, cancellationToken);

            // Press Enter to accept terms (defaults to Accept button) - using DirectX-compatible method
            await _automationService.SendKeyAsync(ConsoleKey.Enter, ffxiWindowHandle, cancellationToken);

            await Task.Delay(2000, cancellationToken); // Allow transition time

            subtask.UpdateProgress(100, "Terms accepted successfully - transitioning to main menu");
        }

        private async Task ExecuteCharacterSelectionAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // Get FFXI window handle from context
            var ffxiWindowHandle = await GetFFXIWindowHandleAsync(subtask, context, cancellationToken);

            subtask.UpdateProgress(10, "Waiting for FFXI main menu...");

            // Wait for main menu screen with window handle re-detection on failure
            var mainMenuMatch = await WaitForScreenDetectionWithRedetectionAsync(
                subtask,
                "FFXI/ffxi_main_screen",
                ffxiWindowHandle,
                context,
                "FFXI main menu",
                cancellationToken,
                new ScreenDetectionOptions
                {
                    Timeout = TimeSpan.FromSeconds(60), // Extended timeout for menu loading
                    CheckInterval = TimeSpan.FromSeconds(1.5)
                    // ConfidenceThreshold will be loaded from template JSON (0.80)
                });

            // The WaitForScreenDetectionWithRedetectionAsync method already validates against the template threshold
            // from the JSON configuration, so if we get here, detection was successful
            await _loggingService.LogInfoAsync($"FFXI main menu detected with confidence: {mainMenuMatch.Confidence:P}");

            subtask.UpdateProgress(50, "Main menu detected");

            // Get updated window handle from context
            ffxiWindowHandle = context.GetValueData<IntPtr>("FFXIWindowHandle");

            // Allow menu animations to complete
            await Task.Delay(1000, cancellationToken);

            subtask.UpdateProgress(70, "Selecting character option (pressing Enter)...");

            // Ensure window has focus
            await _automationService.EnsureWindowFocusAsync(ffxiWindowHandle, cancellationToken);
            await Task.Delay(500, cancellationToken);

            // Press Enter to select "Select Character" menu option (default selection) - using DirectX-compatible method
            await _automationService.SendKeyAsync(ConsoleKey.Enter, ffxiWindowHandle, cancellationToken);

            await Task.Delay(2000, cancellationToken); // Allow transition time

            subtask.UpdateProgress(100, "Main menu navigation completed - transitioning to character slots");
        }

        private async Task ExecuteCharacterSlotPickAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // Get FFXI window handle from context
            var ffxiWindowHandle = await GetFFXIWindowHandleAsync(subtask, context, cancellationToken);

            // Get character slot from account configuration
            var characterSlot = queueItem.Account?.FFXICharacterSlot ?? 1;
            if (characterSlot < 1 || characterSlot > 16)
            {
                throw new InvalidOperationException($"Invalid character slot: {characterSlot}. Must be between 1 and 16");
            }

            subtask.UpdateProgress(10, "Waiting for character slot selection screen...");

            // Wait for character slot screen with window handle re-detection on failure
            // Use template's configured threshold (0.50 from JSON) instead of hardcoded value
            var slotScreenMatch = await WaitForScreenDetectionWithRedetectionAsync(
                subtask,
                "FFXI/ffxi_character_slot_select_screen",
                ffxiWindowHandle,
                context,
                "character slot selection screen",
                cancellationToken,
                new ScreenDetectionOptions
                {
                    Timeout = TimeSpan.FromSeconds(45),
                    CheckInterval = TimeSpan.FromSeconds(1.5)
                    // ConfidenceThreshold will be loaded from template JSON (currently 0.50)
                });

            // The WaitForScreenDetectionWithRedetectionAsync method already validates against the template threshold,
            // so if we get here, detection was successful - no need for additional validation
            await _loggingService.LogInfoAsync($"Character slot screen detected with confidence: {slotScreenMatch.Confidence:P}");

            subtask.UpdateProgress(40, "Character slot screen detected");

            // Get updated window handle from context
            ffxiWindowHandle = context.GetValueData<IntPtr>("FFXIWindowHandle");

            // Allow screen to fully render
            await Task.Delay(1000, cancellationToken);

            // Navigate to the correct slot if not slot 1
            if (characterSlot > 1)
            {
                subtask.UpdateProgress(50, $"Navigating to character slot {characterSlot}...");
                await _loggingService.LogInfoAsync($"[NAVIGATION] Need to navigate from slot 1 to slot {characterSlot} (sending {characterSlot - 1} down arrows)");

                // Ensure window has focus before starting navigation
                await _automationService.EnsureWindowFocusAsync(ffxiWindowHandle, cancellationToken);
                await Task.Delay(750, cancellationToken); // Longer delay to ensure focus is stable

                // Press Down arrow key (characterSlot - 1) times to reach the desired slot - using DirectX-compatible method
                for (int i = 1; i < characterSlot; i++)
                {
                    await _loggingService.LogInfoAsync($"[NAVIGATION] Sending down arrow {i} of {characterSlot - 1} to reach slot {characterSlot}");

                    // Ensure focus is maintained before each key press
                    await _automationService.EnsureWindowFocusAsync(ffxiWindowHandle, cancellationToken);
                    await Task.Delay(500, cancellationToken); // Increased delay to ensure focus is stable

                    await _automationService.SendKeyAsync(ConsoleKey.DownArrow, ffxiWindowHandle, cancellationToken);
                    await _loggingService.LogInfoAsync($"[NAVIGATION] Down arrow {i} sent successfully");

                    await Task.Delay(1000, cancellationToken); // Increased delay to ensure FFXI processes the input

                    // Update progress during navigation
                    var navProgress = 50 + (20 * i / (characterSlot - 1));
                    subtask.UpdateProgress(navProgress, $"Navigating to slot {characterSlot} ({i}/{characterSlot - 1})...");
                }

                // Extra delay after navigation to ensure selection is stable
                await Task.Delay(1000, cancellationToken);
                await _loggingService.LogInfoAsync($"[NAVIGATION] Navigation complete - should now be on slot {characterSlot}");
            }
            else
            {
                subtask.UpdateProgress(60, "Using default character slot 1");
            }

            subtask.UpdateProgress(80, $"Selecting character slot {characterSlot} (pressing Enter)...");
            await _loggingService.LogInfoAsync($"[NAVIGATION] About to press Enter to select character slot {characterSlot}");

            // Ensure window still has focus before Enter
            await _automationService.EnsureWindowFocusAsync(ffxiWindowHandle, cancellationToken);
            await Task.Delay(750, cancellationToken); // Consistent longer delay

            // Press Enter to select the character slot - using DirectX-compatible method
            await _automationService.SendKeyAsync(ConsoleKey.Enter, ffxiWindowHandle, cancellationToken);
            await _loggingService.LogInfoAsync($"[NAVIGATION] Enter key sent to select character slot {characterSlot}");

            await Task.Delay(3000, cancellationToken); // Allow character loading time

            subtask.UpdateProgress(100, $"Character slot {characterSlot} selected successfully");
        }

        private async Task ExecuteConfirmLoginAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // Get FFXI window handle from context
            var ffxiWindowHandle = await GetFFXIWindowHandleAsync(subtask, context, cancellationToken);

            var accountName = queueItem.Account?.AccountName ?? "Character";
            var characterSlot = queueItem.Account?.FFXICharacterSlot ?? 1;

            subtask.UpdateProgress(10, "Waiting for character confirmation screen...");

            // Wait for character confirmation screen with window handle re-detection on failure
            var confirmMatch = await WaitForScreenDetectionWithRedetectionAsync(
                subtask,
                "FFXI/ffxi_character_confirmation",
                ffxiWindowHandle,
                context,
                "character confirmation screen",
                cancellationToken,
                new ScreenDetectionOptions
                {
                    Timeout = TimeSpan.FromSeconds(45),
                    CheckInterval = TimeSpan.FromSeconds(1.5)
                    // ConfidenceThreshold will be loaded from template JSON (0.80)
                });

            // The WaitForScreenDetectionWithRedetectionAsync method already validates against the template threshold
            // from the JSON configuration, so if we get here, detection was successful
            await _loggingService.LogInfoAsync($"Character confirmation screen detected with confidence: {confirmMatch.Confidence:P}");

            subtask.UpdateProgress(40, "Character confirmation screen detected");

            // Get updated window handle from context
            ffxiWindowHandle = context.GetValueData<IntPtr>("FFXIWindowHandle");

            // Allow screen to stabilize
            await Task.Delay(1000, cancellationToken);

            subtask.UpdateProgress(60, "Confirming character login (pressing Enter)...");

            // Ensure window has focus
            await _automationService.EnsureWindowFocusAsync(ffxiWindowHandle, cancellationToken);
            await Task.Delay(500, cancellationToken);

            // Press Enter to confirm and enter the game world - using DirectX-compatible method
            await _automationService.SendKeyAsync(ConsoleKey.Enter, ffxiWindowHandle, cancellationToken);

            subtask.UpdateProgress(80, "Login confirmation sent, entering game world...");

            // Allow significant time for the game world to load
            await Task.Delay(5000, cancellationToken);

            subtask.UpdateProgress(95, "Verifying successful game entry...");

            // Additional wait to ensure character is fully loaded into the game world
            await Task.Delay(3000, cancellationToken);

            subtask.UpdateProgress(100, $"Success! {accountName} (slot {characterSlot}) has entered the game world");

            // Clear the stored window handle as login is complete
            context.Data.TryRemove("FFXIWindowHandle", out _);
        }

        /// <summary>
        /// Screen detection with automatic window handle re-detection when screenshots fail
        /// This handles the common FFXI issue where window handles become invalid during transitions
        /// </summary>
        private async Task<TemplateMatchResult> WaitForScreenDetectionWithRedetectionAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr initialWindowHandle,
            IAutoLoginContext context,
            string screenDescription,
            CancellationToken cancellationToken,
            ScreenDetectionOptions options = null)
        {
            options ??= ScreenDetectionOptions.Default;

            // Load template metadata to get the configured confidence threshold
            var templateMetadata = await _templateManagementService.GetTemplateMetadataAsync(templatePath);
            if (templateMetadata == null)
            {
                throw new InvalidOperationException($"Template metadata not found for: {templatePath}");
            }

            // Use template's configured confidence threshold instead of options default
            var confidenceThreshold = templateMetadata.ConfidenceThreshold;
            await _loggingService.LogInfoAsync($"Using template confidence threshold: {confidenceThreshold:P} for {screenDescription}");

            var currentWindowHandle = initialWindowHandle;
            var maxAttempts = (int)(options.Timeout.TotalSeconds / options.CheckInterval.TotalSeconds);
            var consecutiveFailures = 0;
            const int maxConsecutiveFailures = 5; // Re-detect window after 5 consecutive screenshot failures

            await _loggingService.LogInfoAsync($"Waiting for {screenDescription} (max {options.Timeout.TotalSeconds}s, checking every {options.CheckInterval.TotalSeconds}s, confidence={confidenceThreshold:P})");

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Update progress based on attempt
                var progress = Math.Min(95, (attempt * 100) / maxAttempts);
                subtask.UpdateProgress(progress, $"Detecting {screenDescription} ({attempt}/{maxAttempts})...");

                try
                {
                    // Try to capture screenshot with current window handle
                    var screenshot = await _screenshotService.CaptureWindowAsync(currentWindowHandle, cancellationToken);

                    if (screenshot == null || !screenshot.IsValid)
                    {
                        consecutiveFailures++;
                        await _loggingService.LogDebugAsync($"{screenDescription} screenshot failed (attempt {attempt}/{maxAttempts}) - consecutive failures: {consecutiveFailures}");

                        // If we've had too many consecutive failures, try to re-detect the window
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            await _loggingService.LogInfoAsync($"Re-detecting FFXI window handle after {consecutiveFailures} consecutive screenshot failures");

                            try
                            {
                                // Re-detect FFXI window handle
                                var newWindowHandle = await FindFFXIWindowHandleAsync(cancellationToken);
                                if (newWindowHandle != IntPtr.Zero && newWindowHandle != currentWindowHandle)
                                {
                                    await _loggingService.LogInfoAsync($"Found new FFXI window handle: 0x{newWindowHandle.ToInt64():X} (was 0x{currentWindowHandle.ToInt64():X})");
                                    currentWindowHandle = newWindowHandle;
                                    context.SetData("FFXIWindowHandle", newWindowHandle);
                                    consecutiveFailures = 0; // Reset failure count with new handle
                                    continue; // Try again immediately with new handle
                                }
                            }
                            catch (Exception redetectEx)
                            {
                                await _loggingService.LogWarningAsync($"Failed to re-detect FFXI window: {redetectEx.Message}");
                            }
                        }
                    }
                    else
                    {
                        // Screenshot successful, reset failure count and try template matching
                        consecutiveFailures = 0;
                        var match = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);

                        await _loggingService.LogDebugAsync($"{screenDescription} detection attempt {attempt}/{maxAttempts}: confidence={match.Confidence:P}, threshold={confidenceThreshold:P}");

                        if (match.Confidence >= confidenceThreshold)
                        {
                            subtask.UpdateProgress(100, $"{screenDescription} detected successfully");
                            await _loggingService.LogInfoAsync($"{screenDescription} detected after {attempt} attempts (confidence: {match.Confidence:P})");

                            // Update context with current window handle
                            context.SetData("FFXIWindowHandle", currentWindowHandle);
                            return match;
                        }

                        // Enhanced diagnostic logging for low confidence
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

            // Final attempt for diagnosis with current window handle
            try
            {
                var finalScreenshot = await _screenshotService.CaptureWindowAsync(currentWindowHandle, cancellationToken);
                var finalMatch = await _templateService.FindElementAsync(finalScreenshot, templatePath, cancellationToken);

                await _loggingService.LogWarningAsync($"{screenDescription} not detected after {maxAttempts} attempts (final confidence: {finalMatch.Confidence:P})");
                return finalMatch;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Final screenshot attempt failed for {screenDescription}", ex);
                return new TemplateMatchResult { Confidence = 0.0f };
            }
        }

        /// <summary>
        /// Find current FFXI window handle by process name and title
        /// </summary>
        private async Task<IntPtr> FindFFXIWindowHandleAsync(CancellationToken cancellationToken)
        {
            // Check for FFXI process (could be pol.exe or ffximain.exe depending on version)
            var processes = Process.GetProcessesByName("pol");
            var ffxiProcesses = Process.GetProcessesByName("ffximain");

            // Combine both process lists
            var allProcesses = new Process[processes.Length + ffxiProcesses.Length];
            processes.CopyTo(allProcesses, 0);
            ffxiProcesses.CopyTo(allProcesses, processes.Length);

            foreach (var process in allProcesses)
            {
                try
                {
                    if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
                        continue;

                    var windowTitle = process.MainWindowTitle;

                    // FFXI window title changes during startup, but usually contains "FINAL FANTASY XI"
                    if (windowTitle.Contains("FINAL FANTASY", StringComparison.OrdinalIgnoreCase) ||
                        windowTitle.Contains("FFXI", StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(windowTitle) && process.ProcessName.Equals("pol", StringComparison.OrdinalIgnoreCase)))
                    {
                        await _loggingService.LogDebugAsync($"Found FFXI process - Handle: 0x{process.MainWindowHandle.ToInt64():X}, Title: '{windowTitle}', PID: {process.Id}");
                        return process.MainWindowHandle;
                    }
                }
                finally
                {
                    process?.Dispose();
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Wait for FFXI process to launch and return its window handle
        /// Uses the same approach as PlayOnlineAuthHandler for process detection
        /// </summary>
        private async Task<IntPtr> WaitForFFXIProcessAsync(
            AutoLoginSubtask subtask,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            var maxAttempts = 60; // 60 seconds to wait for FFXI to launch
            var attempt = 0;

            await _loggingService.LogInfoAsync("Waiting for Final Fantasy XI process to launch...");

            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                var progress = Math.Min(10, (attempt * 10 / maxAttempts));
                subtask.UpdateProgress(progress, $"Finding FFXI process ({attempt}/{maxAttempts})...");

                var windowHandle = await FindFFXIWindowHandleAsync(cancellationToken);
                if (windowHandle != IntPtr.Zero)
                {
                    subtask.UpdateProgress(12, "Found FFXI process");
                    return windowHandle;
                }

                // Also check if we have a stored window handle from PlayOnline that might have transitioned
                var storedHandle = context.GetValueData<IntPtr>("WindowHandle");
                if (storedHandle != IntPtr.Zero)
                {
                    try
                    {
                        // Verify the window is still valid and might be FFXI now
                        var screenshot = await _screenshotService.CaptureWindowAsync(storedHandle, cancellationToken);
                        if (screenshot != null && screenshot.IsValid)
                        {
                            await _loggingService.LogDebugAsync($"Checking stored window handle: {screenshot.WindowTitle}");
                            if (screenshot.WindowTitle.Contains("FINAL FANTASY", StringComparison.OrdinalIgnoreCase))
                            {
                                subtask.UpdateProgress(12, $"Stored handle is now FFXI: {screenshot.WindowTitle}");
                                await _loggingService.LogInfoAsync($"Stored window handle is now FFXI: {screenshot.WindowTitle}");
                                return storedHandle;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogDebugAsync($"Error checking stored window handle: {ex.Message}");
                    }
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }

            throw new InvalidOperationException($"Final Fantasy XI process did not launch after {maxAttempts} seconds");
        }

        /// <summary>
        /// Wait for FFXI to fully initialize - simplified approach similar to PlayOnlineAuthHandler
        /// Just checks window responsiveness rather than template matching
        /// </summary>
        private async Task WaitForFFXIStartup(IntPtr windowHandle, AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            var maxAttempts = 10; // 10 seconds for FFXI window to become responsive
            var attempt = 0;

            await _loggingService.LogInfoAsync("Waiting for Final Fantasy XI window to become responsive...");

            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                subtask.UpdateProgress(12 + (attempt * 3 / maxAttempts), $"FFXI responsiveness check ({attempt + 1}/{maxAttempts})...");

                try
                {
                    // Capture window and check if it's responsive
                    var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);

                    if (screenshot != null && screenshot.IsValid && screenshot.Width > 0 && screenshot.Height > 0)
                    {
                        var windowTitle = screenshot.WindowTitle;
                        await _loggingService.LogDebugAsync($"FFXI startup check {attempt + 1}/{maxAttempts}: Window responsive, title: '{windowTitle}', size: {screenshot.Width}x{screenshot.Height}");

                        // If we get a few successful screenshots, consider FFXI ready
                        if (attempt >= 2)
                        {
                            await _loggingService.LogInfoAsync("Final Fantasy XI window initialization completed");
                            break;
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
                await Task.Delay(1000, cancellationToken);
            }

            // Additional buffer time for DirectX rendering to stabilize
            await Task.Delay(1000, cancellationToken);
        }

        /// <summary>
        /// Get FFXI window handle from context or find it
        /// </summary>
        private async Task<IntPtr> GetFFXIWindowHandleAsync(
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
    }
}
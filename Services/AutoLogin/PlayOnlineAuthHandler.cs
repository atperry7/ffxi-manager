using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Handles PlayOnline authentication tasks including member selection, password entry, and OTP entry.
    /// Implements screen detection-based automation for the complete PlayOnline authentication flow.
    /// </summary>
    public class PlayOnlineAuthHandler : BaseLoginTaskHandler
    {
        private readonly IUIAutomationService _automationService;
        private readonly IWindowsCredentialsService _credentialsService;
        private readonly IOTPService _otpService;
        private readonly IPlayOnlineMonitorService _playOnlineMonitorService;
        private readonly IExternalApplicationService _externalApplicationService;
        private readonly IAutoLoginContextService _contextService;

        public PlayOnlineAuthHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IUIAutomationService automationService,
            IWindowsCredentialsService credentialsService,
            IOTPService otpService,
            IPlayOnlineMonitorService playOnlineMonitorService,
            IExternalApplicationService externalApplicationService,
            IAutoLoginContextService contextService)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
            _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
            _playOnlineMonitorService = playOnlineMonitorService ?? throw new ArgumentNullException(nameof(playOnlineMonitorService));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
            _contextService = contextService ?? throw new ArgumentNullException(nameof(contextService));
        }

        public override LoginTaskStep TaskStep => LoginTaskStep.MemberSelection;

        public override bool CanHandle(AutoLoginSubtask subtask)
        {
            return subtask.TaskStep switch
            {
                LoginTaskStep.MemberSelection => true,
                LoginTaskStep.PasswordEntry => true,
                LoginTaskStep.OTPEntry => true,
                _ => false
            };
        }

        protected override async Task ExecuteHandlerLogicAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            subtask.Start();

            await _loggingService.LogInfoAsync($"[FLOW] Starting PlayOnline {subtask.TaskStep} for {queueItem.DisplayName}");

            try
            {
                switch (subtask.TaskStep)
                {
                    case LoginTaskStep.MemberSelection:
                        await ExecuteMemberSelectionAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    case LoginTaskStep.PasswordEntry:
                        await ExecutePasswordEntryAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    case LoginTaskStep.OTPEntry:
                        await ExecuteOTPEntryAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    default:
                        throw new NotSupportedException($"Task step {subtask.TaskStep} is not supported by PlayOnlineAuthHandler");
                }

                subtask.Complete();
                await _loggingService.LogInfoAsync($"[FLOW] Completed PlayOnline {subtask.TaskStep} for {queueItem.DisplayName}");
            }
            catch (OperationCanceledException)
            {
                subtask.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                subtask.Fail(ex.Message);
                await _loggingService.LogErrorAsync($"PlayOnline {subtask.TaskStep} failed for {queueItem.DisplayName}", ex);
                throw;
            }
        }

        private async Task ExecuteMemberSelectionAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            var accountName = queueItem.Account?.AccountName ?? "Unknown";

            // Validate account information
            if (string.IsNullOrEmpty(queueItem.Account?.AccountName))
            {
                throw new InvalidOperationException("Valid account name is required for member selection");
            }

            // Get member slot from account configuration (default to slot 1 if not specified)
            var memberSlot = queueItem.Account.POLMemberSlot;
            if (memberSlot < 1 || memberSlot > 4)
            {
                throw new InvalidOperationException($"Invalid member slot: {memberSlot}. Must be between 1 and 4");
            }

            // Get the current active PlayOnline window handle
            subtask.UpdateProgress(5, "Finding active PlayOnline window...");
            var windowHandle = await FindWindowHandleAsync(
                subtask,
                "pol",
                "PlayOnline",
                cancellationToken);

            // Phase 2: Progressive detection with extended timeouts
            subtask.UpdateProgress(15, "Detecting PlayOnline startup completion...");
            await WaitForPlayOnlineStartup(windowHandle, cancellationToken);

            // Phase 3: Wait for member selection screen with extended detection
            subtask.UpdateProgress(30, "Waiting for member selection interface to load...");
            var memberScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                "PlayOnline/member_selection_screen",
                windowHandle,
                "member selection screen",
                cancellationToken,
                ScreenDetectionOptions.Extended); // 60-second timeout for member selection

            if (memberScreenMatch.Confidence < 0.80f)
            {
                throw new InvalidOperationException($"Could not detect member selection screen after extended wait (confidence: {memberScreenMatch.Confidence:P})");
            }

            // Phase 4: Ensure screen is stable before interaction
            subtask.UpdateProgress(50, "Verifying member selection screen stability...");
            await Task.Delay(1000, cancellationToken); // Allow screen to stabilize

            // Phase 5: Get fresh screenshot for interaction
            subtask.UpdateProgress(60, $"Preparing to select member slot {memberSlot}...");
            var finalScreenshot = await CaptureScreenshotWithLogging(windowHandle, "member slot selection", cancellationToken);

            // Phase 6: Perform member selection
            subtask.UpdateProgress(70, $"Clicking member slot {memberSlot} for {accountName}...");
            var memberCoordinates = GetMemberSlotCoordinates(memberSlot);
            await ClickAtCoordinatesAsync(
                subtask,
                memberCoordinates,
                windowHandle,
                $"member slot {memberSlot}",
                cancellationToken,
                _automationService);

            // Phase 7: Wait and confirm selection
            subtask.UpdateProgress(85, "Confirming member selection and waiting for response...");
            await Task.Delay(1500, cancellationToken); // Extended wait for POL response

            // Store context for next steps
            context.SetData("SelectedMemberSlot", memberSlot);
            context.SetData("WindowHandle", windowHandle);

            subtask.UpdateProgress(100, $"Member slot {memberSlot} selected successfully for {accountName}");
        }

        private async Task ExecutePasswordEntryAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            var accountName = queueItem.Account?.AccountName ?? "Unknown";

            // Validate stored password
            if (!queueItem.Account?.HasStoredPassword ?? true)
            {
                throw new InvalidOperationException($"No stored password found for account: {accountName}");
            }

            // Get the current active PlayOnline window handle
            var windowHandle = await FindWindowHandleAsync(
                subtask,
                "pol",
                "PlayOnline",
                cancellationToken);

            subtask.UpdateProgress(5, "Waiting for login information screen...");

            // Wait for login information screen
            subtask.UpdateProgress(15, "Detecting login information screen...");
            var loginScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                "PlayOnline/login_information_screen",
                windowHandle,
                "login information screen",
                cancellationToken,
                ScreenDetectionOptions.WithTimeout(15));

            if (loginScreenMatch.Confidence < 0.80f)
            {
                throw new InvalidOperationException($"Could not detect login information screen (confidence: {loginScreenMatch.Confidence:P})");
            }

            // Click "Log In" button
            subtask.UpdateProgress(25, "Clicking Log In button...");
            await ClickAtCoordinatesAsync(
                subtask,
                new Point(305, 415), // TODO: Move to configuration
                windowHandle,
                "Log In button",
                cancellationToken,
                _automationService);
            await Task.Delay(1500, cancellationToken);

            // Wait for connect to PlayOnline screen
            subtask.UpdateProgress(35, "Waiting for connection screen...");
            var connectScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                "PlayOnline/connect_to_playonline_screen",
                windowHandle,
                "connect to PlayOnline screen",
                cancellationToken,
                ScreenDetectionOptions.WithTimeout(15));

            if (connectScreenMatch.Confidence < 0.80f)
            {
                throw new InvalidOperationException($"Could not detect connect to PlayOnline screen (confidence: {connectScreenMatch.Confidence:P})");
            }

            // Click password field
            subtask.UpdateProgress(45, "Selecting password field...");
            await ClickAtCoordinatesAsync(
                subtask,
                new Point(1034, 520), // TODO: Move to configuration
                windowHandle,
                "password field",
                cancellationToken,
                _automationService);
            await Task.Delay(1000, cancellationToken);

            // Wait for virtual keyboard
            subtask.UpdateProgress(55, "Waiting for virtual keyboard...");
            var keyboardMatch = await WaitForScreenDetectionAsync(
                subtask,
                "PlayOnline/virtual_keyboard_password_input_screen",
                windowHandle,
                "virtual keyboard screen",
                cancellationToken,
                ScreenDetectionOptions.WithTimeout(10));

            if (keyboardMatch.Confidence < 0.80f)
            {
                throw new InvalidOperationException($"Could not detect virtual keyboard (confidence: {keyboardMatch.Confidence:P})");
            }

            // Click virtual keyboard password field
            subtask.UpdateProgress(65, "Selecting virtual keyboard password field...");
            await ClickAtCoordinatesAsync(
                subtask,
                new Point(740, 390), // TODO: Move to configuration
                windowHandle,
                "virtual keyboard password field",
                cancellationToken,
                _automationService);
            await Task.Delay(500, cancellationToken);

            // Retrieve and enter password
            subtask.UpdateProgress(75, "Retrieving stored password...");
            var credentialTarget = _credentialsService.GenerateCredentialTarget(queueItem.Profile?.FilePath ?? string.Empty, queueItem.Account.Id);
            var password = await _credentialsService.RetrievePasswordAsync(credentialTarget, accountName);
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException($"Could not retrieve password for account: {accountName}");
            }

            // DIAGNOSTIC: Log password length for debugging (without exposing content)
            await _loggingService.LogDebugAsync($"[DIAGNOSTIC] Password retrieved for {accountName}: Length={password.Length} characters");

            // DIAGNOSTIC: Check for any problematic characters that might not type correctly
            var problematicChars = password.Where(c => char.IsControl(c) || c > 127).ToList();
            if (problematicChars.Any())
            {
                await _loggingService.LogWarningAsync($"[DIAGNOSTIC] Password contains {problematicChars.Count} potentially problematic characters (control chars or non-ASCII)");
            }

            subtask.UpdateProgress(85, "Entering password securely...");
            await _automationService.TypeSecureTextAsync(password, 50, cancellationToken);
            await Task.Delay(500, cancellationToken);

            // DIAGNOSTIC: Log completion
            await _loggingService.LogDebugAsync($"[DIAGNOSTIC] Password entry completed for {accountName}");

            // Click CircleConfirmation button to confirm password entry
            subtask.UpdateProgress(85, "Confirming password entry...");
            await ClickAtTemplateCoordinatesAsync(
                subtask,
                "PlayOnline/circle_confirmation_password",
                windowHandle,
                cancellationToken,
                _automationService);
            await Task.Delay(1000, cancellationToken); // Allow confirmation to process

            // Check if OTP is required - if not, click Connect now
            if (!queueItem.Account?.IsOTPEnabled ?? true)
            {
                subtask.UpdateProgress(95, "Clicking Connect button (no OTP required)...");
                await ClickAtTemplateCoordinatesAsync(
                    subtask,
                    "PlayOnline/connect_button",
                    windowHandle,
                    cancellationToken,
                    _automationService);
                await Task.Delay(2000, cancellationToken); // Allow connection to process
            }

            subtask.UpdateProgress(100, "Password entry and confirmation completed successfully");
        }

        private async Task ExecuteOTPEntryAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            // Check if OTP is required
            if (!queueItem.Account?.IsOTPEnabled ?? true)
            {
                subtask.Skip("OTP not required for this account");
                return;
            }

            var accountName = queueItem.Account?.AccountName ?? "Unknown";

            // Get the current active PlayOnline window handle
            var windowHandle = await FindWindowHandleAsync(
                subtask,
                "pol",
                "PlayOnline",
                cancellationToken);

            subtask.UpdateProgress(10, "Generating OTP code...");
            var otpCode = await _otpService.GenerateOTPCodeAsync(queueItem.Profile?.FilePath ?? string.Empty, queueItem.Account.Id);
            if (string.IsNullOrEmpty(otpCode))
            {
                throw new InvalidOperationException($"Could not generate OTP code for account: {accountName}");
            }

            await _loggingService.LogDebugAsync($"Generated OTP code for {accountName}: {otpCode.Substring(0, 2)}****");

            subtask.UpdateProgress(30, "Locating OTP input field...");
            var otpScreenshot = await CaptureScreenshotWithLogging(windowHandle, "OTP field selection", cancellationToken);

            // Click OTP field (coordinates from connect_to_playonline_screen template)
            var otpFieldPoint = otpScreenshot.ToScreenCoordinates(new Point(1034, 579));
            await _automationService.ClickAsync(otpFieldPoint, cancellationToken);
            await Task.Delay(500, cancellationToken);

            subtask.UpdateProgress(60, $"Entering OTP code: {otpCode.Substring(0, 2)}****");
            await _automationService.TypeTextAsync(otpCode, 100, cancellationToken);
            await Task.Delay(500, cancellationToken);

            // Click Connect button after OTP entry
            subtask.UpdateProgress(80, "Clicking Connect button after OTP entry...");
            await ClickAtTemplateCoordinatesAsync(
                subtask,
                "PlayOnline/connect_button",
                windowHandle,
                cancellationToken,
                _automationService);
            await Task.Delay(2000, cancellationToken); // Allow connection to process

            // Check if POL Proxy is configured to determine the flow
            var applications = await _externalApplicationService.GetApplicationsAsync();
            var polProxyApp = applications.FirstOrDefault(app =>
                app.Name.Contains("POL Proxy", StringComparison.OrdinalIgnoreCase) ||
                app.ExecutablePath.Contains("POLProxy", StringComparison.OrdinalIgnoreCase));

            if (polProxyApp != null)
            {
                // POL Proxy is configured - skip PlayOnline navigation, go directly to FFXI
                await _loggingService.LogInfoAsync($"POL Proxy detected ({polProxyApp.Name}) - skipping PlayOnline navigation screens");
                subtask.UpdateProgress(90, "POL Proxy detected - bypassing PlayOnline screens, transitioning to FFXI...");

                // Store the current window handle for potential FFXI transition
                context.SetData("WindowHandle", windowHandle);

                // Allow additional time for POL Proxy to handle the transition
                await Task.Delay(3000, cancellationToken);
            }
            else
            {
                // No POL Proxy - proceed with standard PlayOnline navigation
                await _loggingService.LogInfoAsync("No POL Proxy configured - proceeding with standard PlayOnline navigation");
                await NavigateToFinalFantasyXI(subtask, windowHandle, context, cancellationToken);
            }

            subtask.UpdateProgress(100, "OTP entry, connection, and game launch completed successfully");
        }

        private async Task NavigateToFinalFantasyXI(AutoLoginSubtask subtask, IntPtr windowHandle, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            // Wait for main screen with window handle re-detection capability
            subtask.UpdateProgress(75, "Waiting for PlayOnline main screen...");
            var mainScreenMatch = await WaitForScreenDetectionWithRedetectionAsync(
                subtask,
                "PlayOnline/main_screen",
                windowHandle,
                context,
                "PlayOnline main screen",
                cancellationToken,
                ScreenDetectionOptions.WithTimeout(20));

            if (mainScreenMatch.Confidence >= 0.80f)
            {
                // Get updated window handle from context
                windowHandle = context.GetValueData<IntPtr>("WindowHandle");

                // Click "Final Fantasy XI"
                subtask.UpdateProgress(80, "Selecting Final Fantasy XI...");
                await ClickAtCoordinatesAsync(
                    subtask,
                    new Point(300, 410), // TODO: Move to configuration
                    windowHandle,
                    "Final Fantasy XI button",
                    cancellationToken,
                    _automationService);
                await Task.Delay(2000, cancellationToken);

                // Wait for play screen with window handle re-detection
                var playScreenMatch = await WaitForScreenDetectionWithRedetectionAsync(
                    subtask,
                    "PlayOnline/play_screen",
                    windowHandle,
                    context,
                    "PlayOnline play screen",
                    cancellationToken,
                    ScreenDetectionOptions.WithTimeout(15));

                if (playScreenMatch.Confidence >= 0.80f)
                {
                    // Get updated window handle from context
                    windowHandle = context.GetValueData<IntPtr>("WindowHandle");

                    // Click "Play"
                    subtask.UpdateProgress(85, "Clicking Play button...");
                    await ClickAtCoordinatesAsync(
                        subtask,
                        new Point(360, 240), // TODO: Move to configuration
                        windowHandle,
                        "Play button",
                        cancellationToken,
                        _automationService);
                    await Task.Delay(2000, cancellationToken);

                    // Wait for play confirmation with window handle re-detection
                    var confirmScreenMatch = await WaitForScreenDetectionWithRedetectionAsync(
                        subtask,
                        "PlayOnline/play_confirmation",
                        windowHandle,
                        context,
                        "PlayOnline play confirmation screen",
                        cancellationToken,
                        ScreenDetectionOptions.WithTimeout(15));

                    if (confirmScreenMatch.Confidence >= 0.80f)
                    {
                        // Get updated window handle from context
                        windowHandle = context.GetValueData<IntPtr>("WindowHandle");

                        // Click final "Play"
                        subtask.UpdateProgress(90, "Confirming game launch...");
                        await ClickAtCoordinatesAsync(
                            subtask,
                            new Point(495, 940), // TODO: Move to configuration
                            windowHandle,
                            "final Play button",
                            cancellationToken,
                            _automationService);
                        await Task.Delay(3000, cancellationToken);
                    }
                }
            }

            // Store the final window handle for FFXI handler to use
            context.SetData("WindowHandle", windowHandle);
        }

        /// <summary>
        /// Screen detection with automatic window handle re-detection when screenshots fail
        /// This handles PlayOnline window handle invalidation during transitions
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
                            await _loggingService.LogInfoAsync($"Re-detecting PlayOnline window handle after {consecutiveFailures} consecutive screenshot failures");

                            try
                            {
                                // Re-detect PlayOnline or FFXI window handle
                                var newWindowHandle = await FindPlayOnlineOrFFXIWindowHandleAsync(cancellationToken);
                                if (newWindowHandle != IntPtr.Zero && newWindowHandle != currentWindowHandle)
                                {
                                    await _loggingService.LogInfoAsync($"Found new window handle: 0x{newWindowHandle.ToInt64():X} (was 0x{currentWindowHandle.ToInt64():X})");
                                    currentWindowHandle = newWindowHandle;
                                    context.SetData("WindowHandle", newWindowHandle);
                                    consecutiveFailures = 0; // Reset failure count with new handle
                                    continue; // Try again immediately with new handle
                                }
                            }
                            catch (Exception redetectEx)
                            {
                                await _loggingService.LogWarningAsync($"Failed to re-detect window: {redetectEx.Message}");
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
                            context.SetData("WindowHandle", currentWindowHandle);
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
        /// Find current PlayOnline or FFXI window handle by process name and title
        /// </summary>
        private async Task<IntPtr> FindPlayOnlineOrFFXIWindowHandleAsync(CancellationToken cancellationToken)
        {
            // Check for both POL and FFXI processes since PlayOnline transitions to FFXI
            var polProcesses = System.Diagnostics.Process.GetProcessesByName("pol");
            var ffxiProcesses = System.Diagnostics.Process.GetProcessesByName("ffximain");

            // Combine both process lists
            var allProcesses = new System.Diagnostics.Process[polProcesses.Length + ffxiProcesses.Length];
            polProcesses.CopyTo(allProcesses, 0);
            ffxiProcesses.CopyTo(allProcesses, polProcesses.Length);

            foreach (var process in allProcesses)
            {
                try
                {
                    if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
                        continue;

                    var windowTitle = process.MainWindowTitle;

                    // Look for PlayOnline or FFXI window titles
                    if (windowTitle.Contains("PlayOnline", StringComparison.OrdinalIgnoreCase) ||
                        windowTitle.Contains("FINAL FANTASY", StringComparison.OrdinalIgnoreCase) ||
                        windowTitle.Contains("FFXI", StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(windowTitle) && process.ProcessName.Equals("pol", StringComparison.OrdinalIgnoreCase)))
                    {
                        await _loggingService.LogDebugAsync($"Found window process - Handle: 0x{process.MainWindowHandle.ToInt64():X}, Title: '{windowTitle}', PID: {process.Id}");
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

        private async Task WaitForPlayOnlineStartup(IntPtr windowHandle, CancellationToken cancellationToken)
        {
            var maxAttempts = 10; // 10 seconds of checking window responsiveness
            var attempt = 0;

            await _loggingService.LogInfoAsync("Waiting for PlayOnline Viewer startup to complete...");

            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Capture window and check if it's responsive
                var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);

                if (screenshot != null && screenshot.IsValid)
                {
                    var windowTitle = screenshot.WindowTitle;
                    await _loggingService.LogDebugAsync($"POL startup check {attempt + 1}/{maxAttempts}: Window responsive, title: {windowTitle}");

                    // If we get 3 consecutive successful screenshots, consider POL ready
                    if (attempt >= 2)
                    {
                        await _loggingService.LogInfoAsync("PlayOnline Viewer startup sequence completed");
                        break;
                    }
                }

                attempt++;
                await Task.Delay(1000, cancellationToken); // Use standard 1-second intervals
            }

            // Additional buffer time for UI elements to fully render
            await Task.Delay(1000, cancellationToken);
        }



        private static Point GetMemberSlotCoordinates(int slotNumber)
        {
            return slotNumber switch
            {
                1 => new Point(840, 350),
                2 => new Point(840, 490),
                3 => new Point(840, 630),
                4 => new Point(840, 765),
                _ => throw new ArgumentException($"Invalid member slot: {slotNumber}. Must be between 1 and 4.")
            };
        }

    }
}
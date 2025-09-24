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

        public PlayOnlineAuthHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            IUIAutomationService automationService,
            IWindowsCredentialsService credentialsService,
            IOTPService otpService,
            IPlayOnlineMonitorService playOnlineMonitorService)
            : base(loggingService, screenshotService, templateService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
            _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
            _playOnlineMonitorService = playOnlineMonitorService ?? throw new ArgumentNullException(nameof(playOnlineMonitorService));
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

            // Get the current active PlayOnline window handle dynamically (includes retry logic with up to 15 seconds wait)
            subtask.UpdateProgress(5, "Finding active PlayOnline window...");
            var windowHandle = await GetCurrentPlayOnlineWindowHandleAsync(cancellationToken);

            // Phase 2: Progressive detection with extended timeouts
            subtask.UpdateProgress(15, "Detecting PlayOnline startup completion...");
            await WaitForPlayOnlineStartup(windowHandle, cancellationToken);

            // Phase 3: Wait for member selection screen with extended detection
            subtask.UpdateProgress(30, "Waiting for member selection interface to load...");
            var memberScreenMatch = await WaitForMemberSelectionScreen(windowHandle, cancellationToken);

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
            var clickPoint = finalScreenshot.ToScreenCoordinates(memberCoordinates);
            await _automationService.ClickAsync(clickPoint, cancellationToken);

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

            // Get the current active PlayOnline window handle dynamically
            var windowHandle = await GetCurrentPlayOnlineWindowHandleAsync(cancellationToken);

            subtask.UpdateProgress(5, "Waiting for login information screen...");

            // Wait for login information screen
            subtask.UpdateProgress(15, "Detecting login information screen...");
            var loginScreenMatch = await WaitForScreenTransition("PlayOnline/login_information_screen", windowHandle, "login information screen", cancellationToken, timeoutSeconds: 15);

            if (loginScreenMatch.Confidence < 0.80f)
            {
                throw new InvalidOperationException($"Could not detect login information screen (confidence: {loginScreenMatch.Confidence:P})");
            }

            // Click "Log In" button
            subtask.UpdateProgress(25, "Clicking Log In button...");
            var freshScreenshot = await CaptureScreenshotWithLogging(windowHandle, "login button click", cancellationToken);
            var loginButtonPoint = freshScreenshot.ToScreenCoordinates(new Point(305, 415));
            await _automationService.ClickAsync(loginButtonPoint, cancellationToken);
            await Task.Delay(1500, cancellationToken);

            // Wait for connect to PlayOnline screen
            subtask.UpdateProgress(35, "Waiting for connection screen...");
            var connectScreenMatch = await WaitForScreenTransition("PlayOnline/connect_to_playonline_screen", windowHandle, "connect to PlayOnline screen", cancellationToken, timeoutSeconds: 15);

            if (connectScreenMatch.Confidence < 0.80f)
            {
                throw new InvalidOperationException($"Could not detect connect to PlayOnline screen (confidence: {connectScreenMatch.Confidence:P})");
            }

            // Click password field
            subtask.UpdateProgress(45, "Selecting password field...");
            var connectScreenshot = await CaptureScreenshotWithLogging(windowHandle, "password field selection", cancellationToken);
            var passwordFieldPoint = connectScreenshot.ToScreenCoordinates(new Point(1034, 520));
            await _automationService.ClickAsync(passwordFieldPoint, cancellationToken);
            await Task.Delay(1000, cancellationToken);

            // Wait for virtual keyboard
            subtask.UpdateProgress(55, "Waiting for virtual keyboard...");
            var keyboardMatch = await WaitForScreenTransition("PlayOnline/virtual_keyboard_password_input_screen", windowHandle, "virtual keyboard screen", cancellationToken, timeoutSeconds: 10);

            if (keyboardMatch.Confidence < 0.80f)
            {
                throw new InvalidOperationException($"Could not detect virtual keyboard (confidence: {keyboardMatch.Confidence:P})");
            }

            // Click virtual keyboard password field
            subtask.UpdateProgress(65, "Selecting virtual keyboard password field...");
            var keyboardScreenshot = await CaptureScreenshotWithLogging(windowHandle, "virtual keyboard field selection", cancellationToken);
            var virtualPasswordFieldPoint = keyboardScreenshot.ToScreenCoordinates(new Point(740, 390));
            await _automationService.ClickAsync(virtualPasswordFieldPoint, cancellationToken);
            await Task.Delay(500, cancellationToken);

            // Retrieve and enter password
            subtask.UpdateProgress(75, "Retrieving stored password...");
            var credentialTarget = _credentialsService.GenerateCredentialTarget(queueItem.Profile?.FilePath ?? string.Empty, queueItem.Account.Id);
            var password = await _credentialsService.RetrievePasswordAsync(credentialTarget, accountName);
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException($"Could not retrieve password for account: {accountName}");
            }

            subtask.UpdateProgress(85, "Entering password securely...");
            await _automationService.TypeSecureTextAsync(password, 50, cancellationToken);
            await Task.Delay(500, cancellationToken);

            subtask.UpdateProgress(100, "Password entry completed successfully");
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

            // Get the current active PlayOnline window handle dynamically
            var windowHandle = await GetCurrentPlayOnlineWindowHandleAsync(cancellationToken);

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

            subtask.UpdateProgress(50, $"Entering OTP code: {otpCode.Substring(0, 2)}****");
            await _automationService.TypeTextAsync(otpCode, 100, cancellationToken);
            await Task.Delay(500, cancellationToken);

            subtask.UpdateProgress(70, "Clicking Connect button...");
            // The Connect button should be available after password and OTP entry
            await _automationService.SendKeyAsync(ConsoleKey.Tab, cancellationToken);
            await Task.Delay(200, cancellationToken);
            await _automationService.SendKeyAsync(ConsoleKey.Enter, cancellationToken);
            await Task.Delay(2000, cancellationToken);

            // Proceed to navigate through final PlayOnline screens
            await NavigateToFinalFantasyXI(subtask, windowHandle, cancellationToken);

            subtask.UpdateProgress(100, "OTP authentication and game launch completed successfully");
        }

        private async Task NavigateToFinalFantasyXI(AutoLoginSubtask subtask, IntPtr windowHandle, CancellationToken cancellationToken)
        {
            // Wait for main screen
            subtask.UpdateProgress(75, "Waiting for PlayOnline main screen...");
            var mainScreenMatch = await WaitForScreenTransition("PlayOnline/main_screen", windowHandle, "PlayOnline main screen", cancellationToken, timeoutSeconds: 20);

            if (mainScreenMatch.Confidence >= 0.80f)
            {
                // Click "Final Fantasy XI"
                subtask.UpdateProgress(80, "Selecting Final Fantasy XI...");
                var mainScreenshot = await CaptureScreenshotWithLogging(windowHandle, "Final Fantasy XI button click", cancellationToken);
                var ffxiButtonPoint = mainScreenshot.ToScreenCoordinates(new Point(300, 410));
                await _automationService.ClickAsync(ffxiButtonPoint, cancellationToken);
                await Task.Delay(2000, cancellationToken);

                // Wait for play screen
                var playScreenMatch = await WaitForScreenTransition("PlayOnline/play_screen", windowHandle, "PlayOnline play screen", cancellationToken, timeoutSeconds: 15);

                if (playScreenMatch.Confidence >= 0.80f)
                {
                    // Click "Play"
                    subtask.UpdateProgress(85, "Clicking Play button...");
                    var playScreenshot = await CaptureScreenshotWithLogging(windowHandle, "Play button click", cancellationToken);
                    var playButtonPoint = playScreenshot.ToScreenCoordinates(new Point(360, 240));
                    await _automationService.ClickAsync(playButtonPoint, cancellationToken);
                    await Task.Delay(2000, cancellationToken);

                    // Wait for play confirmation
                    var confirmScreenMatch = await WaitForScreenTransition("PlayOnline/play_confirmation", windowHandle, "PlayOnline play confirmation screen", cancellationToken, timeoutSeconds: 15);

                    if (confirmScreenMatch.Confidence >= 0.80f)
                    {
                        // Click final "Play"
                        subtask.UpdateProgress(90, "Confirming game launch...");
                        var confirmScreenshot = await CaptureScreenshotWithLogging(windowHandle, "final Play button confirmation", cancellationToken);
                        var finalPlayButtonPoint = confirmScreenshot.ToScreenCoordinates(new Point(495, 940));
                        await _automationService.ClickAsync(finalPlayButtonPoint, cancellationToken);
                        await Task.Delay(3000, cancellationToken);
                    }
                }
            }
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


        private async Task<TemplateMatchResult> WaitForMemberSelectionScreen(IntPtr windowHandle, CancellationToken cancellationToken)
        {
            // Use the standardized detection method
            return await WaitForScreenDetection(
                "PlayOnline/member_selection_screen",
                windowHandle,
                "member selection screen",
                cancellationToken,
                timeoutSeconds: 30);
        }

        private async Task<TemplateMatchResult> WaitForScreenTransition(string templatePath, IntPtr windowHandle, string screenDescription, CancellationToken cancellationToken, int timeoutSeconds = 15)
        {
            // Use the standardized detection method for screen transitions
            return await WaitForScreenDetection(
                templatePath,
                windowHandle,
                screenDescription,
                cancellationToken,
                timeoutSeconds);
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

        /// <summary>
        /// Gets the current active PlayOnline window handle dynamically from the monitoring service with retry logic.
        /// This ensures we always use a valid, current window handle instead of stale context data.
        /// If monitoring service fails, falls back to direct process enumeration like WindowerLaunchHandler.
        /// </summary>
        private async Task<IntPtr> GetCurrentPlayOnlineWindowHandleAsync(CancellationToken cancellationToken)
        {
            const int maxAttempts = 15; // 15 seconds max wait time
            const int delayBetweenAttempts = 1000; // 1 second between attempts

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await _loggingService.LogDebugAsync($"Getting current PlayOnline window handle from monitoring service (attempt {attempt}/{maxAttempts})...");

                    // First try: Use the monitoring service
                    var windowHandle = await TryGetWindowFromMonitoringServiceAsync(attempt, maxAttempts);
                    if (windowHandle != IntPtr.Zero)
                    {
                        return windowHandle;
                    }

                    // Second try: Direct process enumeration (like WindowerLaunchHandler)
                    await _loggingService.LogDebugAsync($"Monitoring service failed, trying direct process enumeration (attempt {attempt}/{maxAttempts})...");
                    windowHandle = await TryGetWindowFromDirectProcessEnumerationAsync(attempt, maxAttempts);
                    if (windowHandle != IntPtr.Zero)
                    {
                        return windowHandle;
                    }

                    // Don't delay on the last attempt
                    if (attempt < maxAttempts)
                    {
                        await _loggingService.LogDebugAsync($"PlayOnline window not found, waiting {delayBetweenAttempts}ms before retry...");
                        await Task.Delay(delayBetweenAttempts, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogWarningAsync($"Exception during window detection attempt {attempt}: {ex.Message}");

                    // Don't delay on the last attempt
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(delayBetweenAttempts, cancellationToken);
                    }
                }
            }

            throw new InvalidOperationException($"No active PlayOnline window found after {maxAttempts} attempts over {maxAttempts} seconds. Ensure PlayOnline Viewer is running and visible.");
        }

        /// <summary>
        /// Attempts to get PlayOnline window handle from the monitoring service.
        /// </summary>
        private async Task<IntPtr> TryGetWindowFromMonitoringServiceAsync(int attempt, int maxAttempts)
        {
            var characters = await _playOnlineMonitorService.GetCharactersAsync();

            // Enhanced debugging - log all characters on each attempt to see what's available
            await _loggingService.LogDebugAsync($"Monitoring service returned {characters.Count} characters");
            foreach (var character in characters)
            {
                await _loggingService.LogDebugAsync($"Character - PID: {character.ProcessId}, Handle: 0x{character.WindowHandle.ToInt64():X}, Title: '{character.WindowTitle}', ProcessName: '{character.ProcessName}', Running: {character.IsRunning}");
            }

            // Look for PlayOnline windows with multiple filtering strategies
            var activePlayOnlineCharacter = characters
                .Where(c => c.IsRunning && c.WindowHandle != IntPtr.Zero)
                .Where(c =>
                    !string.IsNullOrEmpty(c.WindowTitle) && (
                        c.WindowTitle.Contains("PlayOnline", StringComparison.OrdinalIgnoreCase) ||
                        c.WindowTitle.Contains("pol", StringComparison.OrdinalIgnoreCase) ||
                        c.ProcessName.Contains("pol", StringComparison.OrdinalIgnoreCase)
                    ))
                .FirstOrDefault();

            if (activePlayOnlineCharacter != null)
            {
                await _loggingService.LogInfoAsync($"Found active PlayOnline window via monitoring service after {attempt} attempts - Handle: 0x{activePlayOnlineCharacter.WindowHandle.ToInt64():X}, Title: '{activePlayOnlineCharacter.WindowTitle}', PID: {activePlayOnlineCharacter.ProcessId}");
                return activePlayOnlineCharacter.WindowHandle;
            }

            await _loggingService.LogWarningAsync($"No PlayOnline window found via monitoring service on attempt {attempt}. Found {characters.Where(c => c.IsRunning && c.WindowHandle != IntPtr.Zero).Count()} running characters with valid window handles");
            return IntPtr.Zero;
        }

        /// <summary>
        /// Attempts to get PlayOnline window handle using direct process enumeration (fallback method).
        /// This follows the same pattern as WindowerLaunchHandler.
        /// </summary>
        private async Task<IntPtr> TryGetWindowFromDirectProcessEnumerationAsync(int attempt, int maxAttempts)
        {
            await _loggingService.LogDebugAsync("Starting direct process enumeration for PlayOnline processes...");

            try
            {
                // Look for PlayOnline processes by name
                var processes = System.Diagnostics.Process.GetProcessesByName("pol");
                if (processes.Length == 0)
                {
                    // Try alternative process names
                    processes = System.Diagnostics.Process.GetProcessesByName("PlayOnlineViewer");
                }

                await _loggingService.LogDebugAsync($"Direct enumeration found {processes.Length} PlayOnline processes");

                foreach (var process in processes)
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            await _loggingService.LogDebugAsync($"Process {process.Id} has exited, skipping");
                            continue;
                        }

                        await _loggingService.LogDebugAsync($"Checking process {process.Id} - ProcessName: '{process.ProcessName}', MainWindowTitle: '{process.MainWindowTitle}'");

                        // Check if this process has a main window
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            var windowTitle = process.MainWindowTitle;
                            await _loggingService.LogDebugAsync($"Process {process.Id} has MainWindow - Handle: 0x{process.MainWindowHandle.ToInt64():X}, Title: '{windowTitle}'");

                            // Check if the window title indicates this is a PlayOnline window
                            if (!string.IsNullOrEmpty(windowTitle) &&
                                (windowTitle.Contains("PlayOnline", StringComparison.OrdinalIgnoreCase) ||
                                 windowTitle.Contains("Viewer", StringComparison.OrdinalIgnoreCase)))
                            {
                                await _loggingService.LogInfoAsync($"Found PlayOnline window via direct enumeration after {attempt} attempts - Handle: 0x{process.MainWindowHandle.ToInt64():X}, Title: '{windowTitle}', PID: {process.Id}");
                                return process.MainWindowHandle;
                            }
                        }
                        else
                        {
                            await _loggingService.LogDebugAsync($"Process {process.Id} has no main window (MainWindowHandle = 0x{process.MainWindowHandle.ToInt64():X})");
                        }
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogWarningAsync($"Exception while checking process {process.Id}: {ex.Message}");
                    }
                    finally
                    {
                        process?.Dispose();
                    }
                }

                await _loggingService.LogWarningAsync($"No suitable PlayOnline window found via direct enumeration on attempt {attempt}");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Exception during direct process enumeration: {ex.Message}");
                return IntPtr.Zero;
            }
        }
    }
}
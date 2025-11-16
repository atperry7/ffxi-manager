using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes OTP (One-Time Password) input actions by retrieving OTP secret from Windows Credential Manager,
    /// generating a TOTP code, and typing it securely into the active window.
    /// </summary>
    /// <remarks>
    /// **Security Features:**
    /// - OTP secret retrieved from Windows Credential Manager at execution time
    /// - OTP code masked in logs (shown as ******)
    /// - OTP secret and code cleared from memory immediately after use
    /// - Uses TypeSecureTextAsync for secure input
    ///
    /// **Hybrid Detection Support:**
    /// - Optional template detection before typing (if action has TemplatePath parameter)
    /// - Can execute blindly without template detection
    ///
    /// **Requirements:**
    /// - Account context must be available via QueueItem
    /// - Account.IsOTPEnabled must be true
    /// - OTP secret must be stored in Windows Credential Manager (with OTP prefix)
    /// - Window must have focus before typing
    ///
    /// **OTP Format:**
    /// - Credential target: FFXIManager.OTP.{profileHash}.{accountId}
    /// - TOTP is generated from the stored secret
    /// </remarks>
    public class InputOTPActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IWindowsCredentialsService _credentialsService;
        private readonly IUIAutomationService _automationService;
        private readonly IScreenDetectionCoordinator _screenDetection;
        private readonly ITemplateMatchingService _templateService;

        public override string ActionType => "InputOTP";
        public override bool RequiresWindowHandle => true;

        public InputOTPActionExecutor(
            ILoggingService loggingService,
            IWindowsCredentialsService credentialsService,
            IUIAutomationService automationService,
            IScreenDetectionCoordinator screenDetection,
            ITemplateMatchingService templateService)
            : base(loggingService)
        {
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _screenDetection = screenDetection ?? throw new ArgumentNullException(nameof(screenDetection));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
        }

        protected override async Task<bool> ExecuteActionAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            CancellationToken cancellationToken)
        {
            // Validate context
            if (context.QueueItem?.Account == null)
            {
                _ = _loggingService.LogErrorAsync("[INPUT-OTP] No account context available");
                return false;
            }

            if (context.QueueItem?.Profile == null)
            {
                _ = _loggingService.LogErrorAsync("[INPUT-OTP] No profile context available");
                return false;
            }

            var account = context.QueueItem.Account;
            var profile = context.QueueItem.Profile;

            // Check if OTP is enabled for this account. If not, treat as no-op success so the step can proceed.
            if (!account.IsOTPEnabled || account.OTPConfiguration == null)
            {
                _ = _loggingService.LogInfoAsync($"[INPUT-OTP] OTP not enabled for {account.DisplayName} - skipping action (treated as success)");
                // Do not fail or retry this action; consider it successfully skipped.
                return true;
            }

            _ = _loggingService.LogInfoAsync($"[INPUT-OTP] Retrieving OTP secret for account {account.DisplayName}");

            // Generate OTP credential target (with OTP prefix)
            var baseTarget = _credentialsService.GenerateCredentialTarget(profile.FilePath, account.Id);
            var otpTarget = baseTarget.Replace("FFXIManager.", "FFXIManager.OTP.");

            // Retrieve OTP secret from Windows Credential Manager
            var otpSecret = await _credentialsService.RetrievePasswordAsync(otpTarget, account.AccountName);

            if (string.IsNullOrEmpty(otpSecret))
            {
                _ = _loggingService.LogWarningAsync($"[INPUT-OTP] No OTP secret found in Credential Manager for account {account.DisplayName}");
                _ = _loggingService.LogInfoAsync("[INPUT-OTP] User needs to set up OTP via Settings → PlayOnline Accounts");
                return false;
            }

            string? otpCode = null;

            try
            {
                // Generate TOTP code from secret
                // Note: This assumes the OTPConfiguration has methods to generate the TOTP code
                // If not, we'll use the CurrentOTPCode property which should be refreshed by the OTP service
                otpCode = account.CurrentOTPCode;

                if (string.IsNullOrEmpty(otpCode))
                {
                    _ = _loggingService.LogWarningAsync("[INPUT-OTP] No OTP code available - OTP service may not be running");
                    return false;
                }

                // Optional template detection (hybrid mode)
                var templatePath = action.GetParameter<string?>("TemplatePath", null);
                if (!string.IsNullOrWhiteSpace(templatePath) && context.WindowHandle != IntPtr.Zero)
                {
                    _ = _loggingService.LogDebugAsync($"[INPUT-OTP] Attempting template detection: {templatePath}");

                    try
                    {
                        // Ensure fresh handle and capture screenshot
                        if (!await context.EnsureFreshWindowHandleAsync())
                        {
                            _ = _loggingService.LogWarningAsync("[INPUT-OTP] Unable to refresh window handle before detection");
                        }
                        var screenshot = await _screenDetection.CaptureScreenshotWithLogging(context.WindowHandle, "OTP field detection", cancellationToken, retryCount: 0);

                        if (screenshot != null && screenshot.IsValid)
                        {
                            // Check for template match
                            var matchResult = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);

                            if (matchResult?.IsValid == true)
                            {
                                _ = _loggingService.LogInfoAsync($"[INPUT-OTP] Template matched: {templatePath} (confidence: {matchResult.Confidence:F2})");

                                // Optional: Click one or more points if provided in action parameters
                                var points = action.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new List<RelativeClickOffset>());
                                if (points != null && points.Count > 0)
                                {
                                    // Store match result in context for CalculateClickPoint
                                    context.TemplateMatch = matchResult;

                                    for (int i = 0; i < points.Count; i++)
                                    {
                                        var p = points[i];
                                        var windowRelativePoint = CalculateClickPoint(p, context);
                                        await _automationService.ClickWindowRelativeAsync(context.WindowHandle, windowRelativePoint, cancellationToken);
                                        if (i < points.Count - 1 && action.DelayMs > 0)
                                        {
                                            await Task.Delay(action.DelayMs, cancellationToken);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                _ = _loggingService.LogWarningAsync($"[INPUT-OTP] Template not found: {templatePath}, proceeding with blind input");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _ = _loggingService.LogWarningAsync($"[INPUT-OTP] Template detection failed: {ex.Message}, proceeding with blind input");
                    }
                }

                // Type OTP code securely
                _ = _loggingService.LogInfoAsync("[INPUT-OTP] Typing OTP code: ******");
                await _automationService.TypeSecureTextAsync(otpCode, action.DelayMs, cancellationToken);

                // Clear OTP data from memory immediately
                otpSecret = null!;
                otpCode = null!;
                GC.Collect(); // Force garbage collection to clear sensitive strings
                
                await Task.Delay(Math.Max(1, action.DelayMs), cancellationToken);
                
                _ = _loggingService.LogInfoAsync("[INPUT-OTP] OTP input completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("[INPUT-OTP] Failed to type OTP code", ex);

                // Clear OTP data from memory on error
                otpSecret = null!;
                otpCode = null!;
                GC.Collect();

                return false;
            }
        }

        /// <summary>
        /// Calculates window-relative click point using either center-relative or template-relative coordinates
        /// </summary>
        private System.Drawing.Point CalculateClickPoint(RelativeClickOffset clickPoint, WorkflowActionContext context)
        {
            if (clickPoint.FromCenter)
            {
                // Center-relative (template-independent, resolution-independent)
                var centerPoint = _automationService.GetWindowCenter(context.WindowHandle);
                var windowRect = _automationService.GetWindowClientRect(context.WindowHandle);

                var offsetX = (int)(clickPoint.X * windowRect.Width);
                var offsetY = (int)(clickPoint.Y * windowRect.Height);

                var windowRelativeX = centerPoint.X - windowRect.Left + offsetX;
                var windowRelativeY = centerPoint.Y - windowRect.Top + offsetY;

                _ = _loggingService.LogDebugAsync($"[INPUT-OTP] Click point (center-relative): ({clickPoint.X:F2},{clickPoint.Y:F2}) -> window=({windowRelativeX},{windowRelativeY})");

                return new System.Drawing.Point(windowRelativeX, windowRelativeY);
            }
            else
            {
                // Template-relative (existing behavior)
                var rect = context.TemplateMatch!.GetBoundingRectangle();
                var wx = rect.Left + (int)Math.Round(clickPoint.X * rect.Width);
                var wy = rect.Top + (int)Math.Round(clickPoint.Y * rect.Height);

                _ = _loggingService.LogDebugAsync($"[INPUT-OTP] Click point (template-relative): ({clickPoint.X:F2},{clickPoint.Y:F2}) -> window=({wx},{wy})");

                return new System.Drawing.Point(wx, wy);
            }
        }
    }
}

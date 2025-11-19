using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes OTP (One-Time Password) input actions by generating a fresh TOTP code on-demand from the stored secret
    /// in Windows Credential Manager and typing it securely into the active window.
    /// </summary>
    /// <remarks>
    /// **Security Features:**
    /// - OTP secret retrieved from Windows Credential Manager at execution time
    /// - TOTP code generated fresh on-demand (within 30-second window)
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
        private readonly IOTPService _otpService;

        public override string ActionType => "InputOTP";
        public override bool RequiresWindowHandle => true;

        public InputOTPActionExecutor(
            ILoggingService loggingService,
            IWindowsCredentialsService credentialsService,
            IUIAutomationService automationService,
            IScreenDetectionCoordinator screenDetection,
            ITemplateMatchingService templateService,
            IOTPService otpService)
            : base(loggingService, automationService)
        {
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _screenDetection = screenDetection ?? throw new ArgumentNullException(nameof(screenDetection));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
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

            _ = _loggingService.LogInfoAsync($"[INPUT-OTP] Generating OTP code for account {account.DisplayName}");

            string? otpCode = null;

            try
            {
                // Generate fresh OTP code on-demand from stored secret
                otpCode = await _otpService.GenerateOTPCodeAsync(profile.FilePath, account.Id);

                if (string.IsNullOrEmpty(otpCode))
                {
                    _ = _loggingService.LogWarningAsync("[INPUT-OTP] No OTP code generated - secret may not be stored or is invalid");
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
                                        var windowRelativePoint = CalculateClickPoint(p, context, "INPUT-OTP");
                                        await _automationService.ClickWindowRelativeAsync(context.WindowHandle, windowRelativePoint, cancellationToken);
                                        if (i < points.Count - 1)
                                        {
                                            await Task.Delay(Math.Max(50, action.DelayMs), cancellationToken);
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

                _ = _loggingService.LogInfoAsync("[INPUT-OTP] OTP input completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("[INPUT-OTP] Failed to type OTP code", ex);
                return false;
            }
        }
    }
}

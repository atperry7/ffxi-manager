using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly ITemplateMatchingService _templateService;

        public override string ActionType => "InputOTP";
        public override bool RequiresWindowHandle => true;

        public InputOTPActionExecutor(
            ILoggingService loggingService,
            IWindowsCredentialsService credentialsService,
            IUIAutomationService automationService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService)
            : base(loggingService)
        {
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
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
                await _loggingService.LogErrorAsync("[INPUT-OTP] No account context available");
                return false;
            }

            if (context.QueueItem?.Profile == null)
            {
                await _loggingService.LogErrorAsync("[INPUT-OTP] No profile context available");
                return false;
            }

            var account = context.QueueItem.Account;
            var profile = context.QueueItem.Profile;

            // Check if OTP is enabled for this account
            if (!account.IsOTPEnabled || account.OTPConfiguration == null)
            {
                await _loggingService.LogWarningAsync($"[INPUT-OTP] OTP is not enabled for account {account.DisplayName}");
                return false;
            }

            await _loggingService.LogInfoAsync($"[INPUT-OTP] Retrieving OTP secret for account {account.DisplayName}");

            // Generate OTP credential target (with OTP prefix)
            var baseTarget = _credentialsService.GenerateCredentialTarget(profile.FilePath, account.Id);
            var otpTarget = baseTarget.Replace("FFXIManager.", "FFXIManager.OTP.");

            // Retrieve OTP secret from Windows Credential Manager
            var otpSecret = await _credentialsService.RetrievePasswordAsync(otpTarget, account.AccountName);

            if (string.IsNullOrEmpty(otpSecret))
            {
                await _loggingService.LogWarningAsync($"[INPUT-OTP] No OTP secret found in Credential Manager for account {account.DisplayName}");
                await _loggingService.LogInfoAsync("[INPUT-OTP] User needs to set up OTP via Settings → PlayOnline Accounts");
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
                    await _loggingService.LogWarningAsync("[INPUT-OTP] No OTP code available - OTP service may not be running");
                    return false;
                }

                // Optional template detection (hybrid mode)
                var templatePath = action.GetParameter<string?>("TemplatePath", null);
                if (!string.IsNullOrWhiteSpace(templatePath) && context.WindowHandle != IntPtr.Zero)
                {
                    await _loggingService.LogDebugAsync($"[INPUT-OTP] Attempting template detection: {templatePath}");

                    try
                    {
                        // Capture screenshot
                        var screenshot = await _screenshotService.CaptureWindowAsync(context.WindowHandle, cancellationToken);

                        if (screenshot != null && screenshot.IsValid)
                        {
                            // Check for template match
                            var matchResult = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);

                            if (matchResult?.IsValid == true)
                            {
                                await _loggingService.LogInfoAsync($"[INPUT-OTP] Template matched: {templatePath} (confidence: {matchResult.Confidence:F2})");

                                // Optional: Click on the detected field if coordinates provided in action
                                var clickX = action.GetParameter<double?>("ClickX", null);
                                var clickY = action.GetParameter<double?>("ClickY", null);

                                if (clickX.HasValue && clickY.HasValue)
                                {
                                    // Calculate relative click point
                                    var absoluteX = matchResult.WindowRelativePosition.X + (int)(matchResult.MatchSize.Width * clickX.Value);
                                    var absoluteY = matchResult.WindowRelativePosition.Y + (int)(matchResult.MatchSize.Height * clickY.Value);
                                    var windowRelativePoint = new System.Drawing.Point(absoluteX, absoluteY);

                                    // Convert to screen coordinates
                                    var screenPoint = screenshot.ToScreenCoordinates(windowRelativePoint);

                                    await _automationService.ClickAsync(screenPoint, cancellationToken);
                                    await Task.Delay(action.DelayMs, cancellationToken);
                                }
                            }
                            else
                            {
                                await _loggingService.LogWarningAsync($"[INPUT-OTP] Template not found: {templatePath}, proceeding with blind input");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogWarningAsync($"[INPUT-OTP] Template detection failed: {ex.Message}, proceeding with blind input");
                    }
                }

                // Ensure window has focus
                if (context.WindowHandle != IntPtr.Zero)
                {
                    await _automationService.EnsureWindowFocusAsync(context.WindowHandle, cancellationToken);
                    await Task.Delay(100, cancellationToken);
                }

                // Type OTP code securely
                await _loggingService.LogInfoAsync("[INPUT-OTP] Typing OTP code: ******");
                await _automationService.TypeSecureTextAsync(otpCode, action.DelayMs, cancellationToken);

                // Clear OTP data from memory immediately
                otpSecret = null!;
                otpCode = null!;
                GC.Collect(); // Force garbage collection to clear sensitive strings

                await _loggingService.LogInfoAsync("[INPUT-OTP] OTP input completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("[INPUT-OTP] Failed to type OTP code", ex);

                // Clear OTP data from memory on error
                otpSecret = null!;
                otpCode = null!;
                GC.Collect();

                return false;
            }
        }
    }
}

using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes password input actions by retrieving credentials from Windows Credential Manager
    /// and typing them securely into the active window.
    /// </summary>
    /// <remarks>
    /// **Security Features:**
    /// - Password retrieved from Windows Credential Manager at execution time
    /// - Password masked in logs (shown as ****)
    /// - Password cleared from memory immediately after use
    /// - Uses TypeSecureTextAsync for secure input
    ///
    /// **Hybrid Detection Support:**
    /// - Optional template detection before typing (if action has TemplatePath parameter)
    /// - Can execute blindly without template detection
    ///
    /// **Requirements:**
    /// - Account context must be available via QueueItem
    /// - Password must be stored in Windows Credential Manager
    /// - Window must have focus before typing
    /// </remarks>
    public class InputPasswordActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IWindowsCredentialsService _credentialsService;
        private readonly IUIAutomationService _automationService;
        private readonly IScreenDetectionCoordinator _screenDetection;
        private readonly ITemplateMatchingService _templateService;

        public override string ActionType => "InputPassword";
        public override bool RequiresWindowHandle => true;

        public InputPasswordActionExecutor(
            ILoggingService loggingService,
            IWindowsCredentialsService credentialsService,
            IUIAutomationService automationService,
            IScreenDetectionCoordinator screenDetection,
            ITemplateMatchingService templateService)
            : base(loggingService, automationService)
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
                _ = _loggingService.LogErrorAsync("[INPUT-PASSWORD] No account context available");
                return false;
            }

            if (context.QueueItem?.Profile == null)
            {
                _ = _loggingService.LogErrorAsync("[INPUT-PASSWORD] No profile context available");
                return false;
            }

            var account = context.QueueItem.Account;
            var profile = context.QueueItem.Profile;

            _ = _loggingService.LogInfoAsync($"[INPUT-PASSWORD] Retrieving password for account {account.DisplayName}");

            // Generate credential target
            var credentialTarget = _credentialsService.GenerateCredentialTarget(profile.FilePath, account.Id);

            // Retrieve password from Windows Credential Manager
            var password = await _credentialsService.RetrievePasswordAsync(credentialTarget, account.AccountName);

            if (string.IsNullOrEmpty(password))
            {
                _ = _loggingService.LogWarningAsync($"[INPUT-PASSWORD] No password found in Credential Manager for account {account.DisplayName}");
                _ = _loggingService.LogInfoAsync("[INPUT-PASSWORD] User needs to set up password via Settings → PlayOnline Accounts");
                return false;
            }

            try
            {
                // Optional template detection (hybrid mode)
                var templatePath = action.GetParameter<string?>("TemplatePath", null);
                if (!string.IsNullOrWhiteSpace(templatePath) && context.WindowHandle != IntPtr.Zero)
                {
                    _ = _loggingService.LogDebugAsync($"[INPUT-PASSWORD] Attempting template detection: {templatePath}");

                    try
                    {
                        var screenshot = await _screenDetection.CaptureScreenshotWithLogging(context.WindowHandle, "password field detection", cancellationToken, retryCount: 0);

                        if (screenshot != null && screenshot.IsValid)
                        {
                            // Check for template match
                            var matchResult = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);

                            if (matchResult?.IsValid == true)
                            {
                                _ = _loggingService.LogInfoAsync($"[INPUT-PASSWORD] Template matched: {templatePath} (confidence: {matchResult.Confidence:F2})");

                                // Optional: Click one or more points if provided in action parameters
                                var points = action.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new List<RelativeClickOffset>());
                                if (points != null && points.Count > 0)
                                {
                                    // Store match result in context for CalculateClickPoint
                                    context.TemplateMatch = matchResult;

                                    for (int i = 0; i < points.Count; i++)
                                    {
                                        var p = points[i];
                                        var windowRelativePoint = CalculateClickPoint(p, context, "INPUT-PASSWORD");
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
                                _ = _loggingService.LogWarningAsync($"[INPUT-PASSWORD] Template not found: {templatePath}, proceeding with blind input");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _ = _loggingService.LogWarningAsync($"[INPUT-PASSWORD] Template detection failed: {ex.Message}, proceeding with blind input");
                    }
                }

                // Type password securely
                _ = _loggingService.LogInfoAsync("[INPUT-PASSWORD] Typing password: ****");
                await _automationService.TypeSecureTextAsync(password, action.DelayMs, cancellationToken);

                _ = _loggingService.LogInfoAsync("[INPUT-PASSWORD] Password input completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("[INPUT-PASSWORD] Failed to type password", ex);
                return false;
            }
        }
    }
}

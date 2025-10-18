using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes member slot selection actions by navigating to the correct PlayOnline member slot.
    /// Supports both keyboard navigation and click-based selection.
    /// </summary>
    /// <remarks>
    /// **Navigation Methods:**
    /// - Keyboard: Use arrow keys to navigate to the target slot (default)
    /// - Click: Use template matching + relative click to select slot
    ///
    /// **Hybrid Detection Support:**
    /// - Optional template detection before navigation (if action has TemplatePath parameter)
    /// - Can execute blindly without template detection
    ///
    /// **Requirements:**
    /// - Account context must be available via QueueItem
    /// - Account.POLMemberSlot must be set (1-4)
    /// - Window must have focus before navigation
    ///
    /// **Parameters:**
    /// - NavigationMethod: "Keyboard" or "Click" (default: "Keyboard")
    /// - TemplatePath: Optional template for slot detection (hybrid mode)
    /// - ClickX, ClickY: Click coordinates if using Click navigation method
    /// </remarks>
    public class MemberSlotActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IUIAutomationService _automationService;
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly ITemplateMatchingService _templateService;

        public override string ActionType => "MemberSlot";
        public override bool RequiresWindowHandle => true;

        public MemberSlotActionExecutor(
            ILoggingService loggingService,
            IUIAutomationService automationService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService)
            : base(loggingService)
        {
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
                await _loggingService.LogErrorAsync("[MEMBER-SLOT] No account context available");
                return false;
            }

            var account = context.QueueItem.Account;
            var targetSlot = account.POLMemberSlot;

            if (targetSlot < 1 || targetSlot > 4)
            {
                await _loggingService.LogErrorAsync($"[MEMBER-SLOT] Invalid member slot: {targetSlot} (must be 1-4)");
                return false;
            }

            await _loggingService.LogInfoAsync($"[MEMBER-SLOT] Navigating to member slot {targetSlot} for account {account.DisplayName}");

            // Get navigation method
            var navigationMethod = action.GetParameter<string>("NavigationMethod", "Keyboard");

            try
            {
                // Optional template detection (hybrid mode)
                TemplateMatchResult? templateMatch = null;
                var templatePath = action.GetParameter<string?>("TemplatePath", null);

                if (!string.IsNullOrWhiteSpace(templatePath) && context.WindowHandle != IntPtr.Zero)
                {
                    await _loggingService.LogDebugAsync($"[MEMBER-SLOT] Attempting template detection: {templatePath}");

                    try
                    {
                        // Capture screenshot
                        var screenshot = await _screenshotService.CaptureWindowAsync(context.WindowHandle, cancellationToken);

                        if (screenshot != null && screenshot.IsValid)
                        {
                            // Check for template match
                            templateMatch = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);

                            if (templateMatch?.IsValid == true)
                            {
                                await _loggingService.LogInfoAsync($"[MEMBER-SLOT] Template matched: {templatePath} (confidence: {templateMatch.Confidence:F2})");
                            }
                            else
                            {
                                await _loggingService.LogWarningAsync($"[MEMBER-SLOT] Template not found: {templatePath}, proceeding with blind navigation");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogWarningAsync($"[MEMBER-SLOT] Template detection failed: {ex.Message}, proceeding with blind navigation");
                    }
                }

                // Ensure window has focus
                if (context.WindowHandle != IntPtr.Zero)
                {
                    await _automationService.EnsureWindowFocusAsync(context.WindowHandle, cancellationToken);
                    await Task.Delay(100, cancellationToken);
                }

                // Execute navigation based on method
                if (navigationMethod.Equals("Click", StringComparison.OrdinalIgnoreCase))
                {
                    await NavigateWithClickAsync(action, context, templateMatch, targetSlot, cancellationToken);
                }
                else // Default to Keyboard
                {
                    await NavigateWithKeyboardAsync(action, context, targetSlot, cancellationToken);
                }

                await _loggingService.LogInfoAsync($"[MEMBER-SLOT] Successfully navigated to member slot {targetSlot}");
                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"[MEMBER-SLOT] Failed to navigate to member slot {targetSlot}", ex);
                return false;
            }
        }

        /// <summary>
        /// Navigates to the target slot using keyboard arrow keys.
        /// Assumes the UI starts at slot 1 (or that the current position is unknown).
        /// </summary>
        private async Task NavigateWithKeyboardAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            int targetSlot,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogDebugAsync($"[MEMBER-SLOT] Using keyboard navigation to slot {targetSlot}");

            // Strategy: Press Home to go to slot 1, then Down arrow (targetSlot - 1) times
            // This ensures we start from a known position

            // Press Home to go to first slot
            await _automationService.SendKeyAsync(ConsoleKey.Home, cancellationToken);
            await Task.Delay(action.DelayMs, cancellationToken);

            // Press Down arrow to navigate to target slot (0-indexed from slot 1)
            var downPresses = targetSlot - 1;
            for (int i = 0; i < downPresses; i++)
            {
                await _automationService.SendKeyAsync(ConsoleKey.DownArrow, cancellationToken);
                await Task.Delay(action.DelayMs, cancellationToken);
            }

            await _loggingService.LogDebugAsync($"[MEMBER-SLOT] Keyboard navigation completed: {downPresses} down arrow presses");
        }

        /// <summary>
        /// Navigates to the target slot by clicking on it.
        /// Requires template match or explicit click coordinates.
        /// </summary>
        private async Task NavigateWithClickAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            TemplateMatchResult? templateMatch,
            int targetSlot,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogDebugAsync($"[MEMBER-SLOT] Using click navigation to slot {targetSlot}");

            // Get click coordinates from action parameters
            var clickX = action.GetParameter<double?>("ClickX", null);
            var clickY = action.GetParameter<double?>("ClickY", null);

            if (!clickX.HasValue || !clickY.HasValue)
            {
                await _loggingService.LogWarningAsync("[MEMBER-SLOT] Click navigation requires ClickX and ClickY parameters");
                throw new InvalidOperationException("Click navigation requires ClickX and ClickY coordinates");
            }

            // Calculate click position
            System.Drawing.Point clickPoint;

            if (templateMatch?.IsValid == true)
            {
                // Capture screenshot for coordinate conversion
                var screenshot = await _screenshotService.CaptureWindowAsync(context.WindowHandle, cancellationToken);
                if (screenshot == null || !screenshot.IsValid)
                {
                    throw new InvalidOperationException("Failed to capture window screenshot for coordinate conversion");
                }

                // Calculate relative click point
                var absoluteX = templateMatch.WindowRelativePosition.X + (int)(templateMatch.MatchSize.Width * clickX.Value);
                var absoluteY = templateMatch.WindowRelativePosition.Y + (int)(templateMatch.MatchSize.Height * clickY.Value);
                var windowRelativePoint = new System.Drawing.Point(absoluteX, absoluteY);

                // Convert to screen coordinates
                clickPoint = screenshot.ToScreenCoordinates(windowRelativePoint);

                await _loggingService.LogDebugAsync($"[MEMBER-SLOT] Clicking at template-relative position: ({clickX.Value:F2}, {clickY.Value:F2})");
            }
            else
            {
                // Fallback: Use window-relative coordinates
                // Note: This assumes the action coordinates are already window-relative
                await _loggingService.LogWarningAsync("[MEMBER-SLOT] No template match available, using window-relative coordinates");

                if (context.WindowHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Click navigation requires valid window handle for fallback mode");
                }

                // Convert relative coordinates (0.0-1.0) to window-relative pixels
                // This requires window size, which we'll need to get
                throw new NotImplementedException("Window-relative click navigation without template match not yet implemented");
            }

            // Perform click
            await _automationService.ClickAsync(clickPoint, cancellationToken);
            await Task.Delay(action.DelayMs, cancellationToken);

            await _loggingService.LogDebugAsync($"[MEMBER-SLOT] Click navigation completed at ({clickPoint.X}, {clickPoint.Y})");
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes character slot selection actions by navigating to the correct FFXI character slot.
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
    /// - Account.FFXICharacterSlot must be set (1-16)
    /// - Window must have focus before navigation
    ///
    /// **Parameters:**
    /// - NavigationMethod: "Keyboard" or "Click" (default: "Keyboard")
    /// - TemplatePath: Optional template for slot detection (hybrid mode)
    /// - ClickX, ClickY: Click coordinates if using Click navigation method
    ///
    /// **Keyboard Navigation Layout:**
    /// Character slots are typically arranged in a 4x4 grid:
    /// - Row 1: Slots 1-4 (Right arrow: slot++)
    /// - Row 2: Slots 5-8 (Down arrow: slot += 4)
    /// - Row 3: Slots 9-12
    /// - Row 4: Slots 13-16
    /// </remarks>
    public class CharacterSlotActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IUIAutomationService _automationService;
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly ITemplateMatchingService _templateService;

        public override string ActionType => "CharacterSlot";

        public CharacterSlotActionExecutor(
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
                await _loggingService.LogErrorAsync("[CHARACTER-SLOT] No account context available");
                return false;
            }

            var account = context.QueueItem.Account;
            var targetSlot = account.FFXICharacterSlot;

            if (targetSlot < 1 || targetSlot > 16)
            {
                await _loggingService.LogErrorAsync($"[CHARACTER-SLOT] Invalid character slot: {targetSlot} (must be 1-16)");
                return false;
            }

            await _loggingService.LogInfoAsync($"[CHARACTER-SLOT] Navigating to character slot {targetSlot} for account {account.DisplayName}");

            // Get navigation method
            var navigationMethod = action.GetParameter<string>("NavigationMethod", "Keyboard");

            try
            {
                // Optional template detection (hybrid mode)
                TemplateMatchResult? templateMatch = null;
                var templatePath = action.GetParameter<string?>("TemplatePath", null);

                if (!string.IsNullOrWhiteSpace(templatePath) && context.WindowHandle != IntPtr.Zero)
                {
                    await _loggingService.LogDebugAsync($"[CHARACTER-SLOT] Attempting template detection: {templatePath}");

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
                                await _loggingService.LogInfoAsync($"[CHARACTER-SLOT] Template matched: {templatePath} (confidence: {templateMatch.Confidence:F2})");
                            }
                            else
                            {
                                await _loggingService.LogWarningAsync($"[CHARACTER-SLOT] Template not found: {templatePath}, proceeding with blind navigation");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogWarningAsync($"[CHARACTER-SLOT] Template detection failed: {ex.Message}, proceeding with blind navigation");
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

                await _loggingService.LogInfoAsync($"[CHARACTER-SLOT] Successfully navigated to character slot {targetSlot}");
                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"[CHARACTER-SLOT] Failed to navigate to character slot {targetSlot}", ex);
                return false;
            }
        }

        /// <summary>
        /// Navigates to the target slot using keyboard arrow keys.
        /// Assumes a 4x4 grid layout: slots arranged as rows of 4 columns.
        /// </summary>
        private async Task NavigateWithKeyboardAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            int targetSlot,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogDebugAsync($"[CHARACTER-SLOT] Using keyboard navigation to slot {targetSlot}");

            // Strategy: Press Home to go to slot 1, then navigate using Right and Down arrows
            // Layout assumption: 4x4 grid
            // - Right arrow: move to next column (slot++)
            // - Down arrow: move to next row (slot += 4)

            // Press Home to go to first slot (slot 1)
            await _automationService.SendKeyAsync(ConsoleKey.Home, cancellationToken);
            await Task.Delay(action.DelayMs, cancellationToken);

            // Calculate grid position (0-indexed)
            int slotIndex = targetSlot - 1; // Convert to 0-indexed
            int row = slotIndex / 4; // 0-3
            int column = slotIndex % 4; // 0-3

            await _loggingService.LogDebugAsync($"[CHARACTER-SLOT] Target grid position: Row {row}, Column {column}");

            // Navigate down to the target row
            for (int i = 0; i < row; i++)
            {
                await _automationService.SendKeyAsync(ConsoleKey.DownArrow, cancellationToken);
                await Task.Delay(action.DelayMs, cancellationToken);
            }

            // Navigate right to the target column
            for (int i = 0; i < column; i++)
            {
                await _automationService.SendKeyAsync(ConsoleKey.RightArrow, cancellationToken);
                await Task.Delay(action.DelayMs, cancellationToken);
            }

            await _loggingService.LogDebugAsync($"[CHARACTER-SLOT] Keyboard navigation completed: {row} down, {column} right");
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
            await _loggingService.LogDebugAsync($"[CHARACTER-SLOT] Using click navigation to slot {targetSlot}");

            // Get click coordinates from action parameters
            var clickX = action.GetParameter<double?>("ClickX", null);
            var clickY = action.GetParameter<double?>("ClickY", null);

            if (!clickX.HasValue || !clickY.HasValue)
            {
                await _loggingService.LogWarningAsync("[CHARACTER-SLOT] Click navigation requires ClickX and ClickY parameters");
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

                await _loggingService.LogDebugAsync($"[CHARACTER-SLOT] Clicking at template-relative position: ({clickX.Value:F2}, {clickY.Value:F2})");
            }
            else
            {
                // Fallback: Use window-relative coordinates
                // Note: This assumes the action coordinates are already window-relative
                await _loggingService.LogWarningAsync("[CHARACTER-SLOT] No template match available, using window-relative coordinates");

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

            await _loggingService.LogDebugAsync($"[CHARACTER-SLOT] Click navigation completed at ({clickPoint.X}, {clickPoint.Y})");
        }
    }
}

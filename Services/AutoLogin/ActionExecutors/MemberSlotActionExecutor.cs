using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes member slot selection using POL's 4-slot visible window pattern.
    /// Handles both direct clicking (slots 1-4) and scroll-then-click (slots 5-20).
    /// </summary>
    /// <remarks>
    /// **POL Slot Display Pattern:**
    /// POL shows 4 member slots at a time in fixed positions. Scrolling down moves the list
    /// up, positioning higher-numbered slots into the 4 visible positions.
    ///
    /// - Slots 1-4: Visible by default, click directly at their physical positions
    /// - Slots 5-20: Scroll down to position target slot at slot 4's position, then click
    ///
    /// **Scroll Formula:**
    /// For slot N where N > 4: Scroll down (N - 4) ticks to position slot N at slot 4's location
    ///
    /// **Click Modes:**
    /// - FromCenter=true: Resolution-independent, template-free (recommended for POL)
    /// - FromCenter=false: Template-relative positioning (legacy support)
    ///
    /// **Requirements:**
    /// - Account context must be available via QueueItem
    /// - Account.POLMemberSlot must be set (1-20)
    /// - ClickPoints array with exactly 4 positions (for the 4 visible slots)
    /// - Workflow must include ScrollWheel reset action BEFORE this action
    ///
    /// **Parameters:**
    /// - ClickPoints (RelativeClickOffset[], required): Array with 4 positions for visible slots
    /// - ScrollTicksPerSlot (int, optional): Ticks per slot when scrolling (default: 1)
    /// - ScrollDelayMs (int, optional): Delay between scroll ticks (default: 100ms)
    /// </remarks>
    public class MemberSlotActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IUIAutomationService _automationService;
        private readonly IScreenDetectionCoordinator _screenDetection;
        private readonly ITemplateMatchingService _templateService;

        public override string ActionType => "MemberSlot";
        public override bool RequiresWindowHandle => true;

        public MemberSlotActionExecutor(
            ILoggingService loggingService,
            IUIAutomationService automationService,
            IScreenDetectionCoordinator screenDetection,
            ITemplateMatchingService templateService)
            : base(loggingService)
        {
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
                _ = _loggingService.LogErrorAsync("[MEMBER-SLOT] No account context available");
                return false;
            }

            var account = context.QueueItem.Account;
            var targetSlot = account.POLMemberSlot;

            if (targetSlot < 1 || targetSlot > 20)
            {
                _ = _loggingService.LogErrorAsync($"[MEMBER-SLOT] Invalid member slot: {targetSlot} (must be 1-20)");
                return false;
            }

            _ = _loggingService.LogInfoAsync($"[MEMBER-SLOT] Selecting member slot {targetSlot} for account {account.DisplayName}");

            try
            {
                // Get configured click points from action parameters (should have exactly 4 positions)
                var clickPoints = action.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new List<RelativeClickOffset>());
                if (clickPoints == null || clickPoints.Count != 4)
                {
                    _ = _loggingService.LogErrorAsync($"[MEMBER-SLOT] ClickPoints must have exactly 4 positions (for visible slots 1-4), but has {clickPoints?.Count ?? 0}");
                    return false;
                }

                // Get scroll configuration
                var scrollTicksPerSlot = action.GetParameter<int>("ScrollTicksPerSlot", 1);
                var scrollDelayMs = action.GetParameter<int>("ScrollDelayMs", 100);

                // Determine navigation strategy based on target slot
                RelativeClickOffset clickPoint;

                if (targetSlot <= 4)
                {
                    // Slots 1-4: Click directly at their physical positions
                    clickPoint = clickPoints[targetSlot - 1];
                    _ = _loggingService.LogDebugAsync($"[MEMBER-SLOT] Slot {targetSlot} is visible, clicking at position {targetSlot}");
                }
                else
                {
                    // Slots 5-20: Scroll down to position target slot, then click at slot 4's position
                    var scrollTicks = (targetSlot - 4) * scrollTicksPerSlot;

                    _ = _loggingService.LogDebugAsync($"[MEMBER-SLOT] Slot {targetSlot} requires scrolling: {scrollTicks} ticks down");

                    // Get window center for scrolling
                    var centerPoint = _automationService.GetWindowCenter(context.WindowHandle);
                    if (centerPoint.IsEmpty)
                    {
                        _ = _loggingService.LogErrorAsync("[MEMBER-SLOT] Failed to get window center for scrolling");
                        return false;
                    }

                    // Move mouse to center (over member slots)
                    await _automationService.MoveMouseAsync(centerPoint, cancellationToken);

                    // Scroll down to position target slot at slot 4's position
                    for (int i = 0; i < scrollTicks; i++)
                    {
                        await _automationService.ScrollMouseWheelAsync(-1, cancellationToken); // Negative = DOWN
                        await Task.Delay(scrollDelayMs, cancellationToken);
                    }

                    // Click at slot 4's position (target slot is now positioned there)
                    clickPoint = clickPoints[3]; // Index 3 = slot 4's position
                    _ = _loggingService.LogDebugAsync($"[MEMBER-SLOT] Scrolled {scrollTicks} ticks, now clicking at slot 4's position");
                }

                // Check if template match is required (only for template-relative clicks)
                if (!clickPoint.FromCenter && context.TemplateMatch == null)
                {
                    _ = _loggingService.LogErrorAsync("[MEMBER-SLOT] Template-relative click requested but no template match available");
                    return false;
                }

                // Calculate click point (supports both template-relative and center-relative)
                System.Drawing.Point screenPoint = CalculateClickPoint(clickPoint, context);

                await _automationService.ClickWindowRelativeAsync(context.WindowHandle, screenPoint, cancellationToken);
                await Task.Delay(Math.Max(1, action.DelayMs), cancellationToken);

                _ = _loggingService.LogInfoAsync($"[MEMBER-SLOT] Successfully selected member slot {targetSlot}");

                // Track state for future optimizations
                account.LastSelectedPOLSlot = targetSlot;

                return true;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync($"[MEMBER-SLOT] Failed to select member slot {targetSlot}", ex);
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

                _loggingService.LogDebugAsync($"[MEMBER-SLOT] Click point (center-relative): ({clickPoint.X:F2},{clickPoint.Y:F2}) -> window=({windowRelativeX},{windowRelativeY})");

                return new System.Drawing.Point(windowRelativeX, windowRelativeY);
            }
            else
            {
                // Template-relative (existing behavior)
                var rect = context.TemplateMatch!.GetBoundingRectangle();
                var wx = rect.Left + (int)Math.Round(clickPoint.X * rect.Width);
                var wy = rect.Top + (int)Math.Round(clickPoint.Y * rect.Height);

                _loggingService.LogDebugAsync($"[MEMBER-SLOT] Click point (template-relative): ({clickPoint.X:F2},{clickPoint.Y:F2}) -> window=({wx},{wy})");

                return new System.Drawing.Point(wx, wy);
            }
        }

    }
}

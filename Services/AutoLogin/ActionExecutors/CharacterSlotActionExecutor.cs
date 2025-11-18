using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes character slot selection actions by clicking the correct FFXI character slot.
    /// Uses template matching combined with relative click coordinates.
    /// </summary>
    /// <remarks>
    /// **Navigation Method (Click-Only):**
    /// - Template detection to locate character selection screen
    /// - Click on specific slot using relative coordinates (0.0-1.0)
    ///
    /// **Requirements:**
    /// - Account context must be available via QueueItem
    /// - Account.FFXICharacterSlot must be set (1-16)
    /// - Window must have focus before navigation
    /// - Action must have TemplatePath parameter for screen detection
    /// - Action must have ClickPoints parameter with 16 click coordinates
    ///
    /// **Parameters:**
    /// - TemplatePath: Template for character selection screen detection (required)
    /// - ClickPoints: Array of 16 relative click coordinates for slots 1-16 (required)
    ///
    /// **Character Slot Layout:**
    /// Slots are arranged in a 4x4 grid:
    /// - Row 1: Slots 1-4
    /// - Row 2: Slots 5-8
    /// - Row 3: Slots 9-12
    /// - Row 4: Slots 13-16
    /// </remarks>
    public class CharacterSlotActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IUIAutomationService _automationService;
        private readonly IScreenDetectionCoordinator _screenDetection;
        private readonly ITemplateMatchingService _templateService;

        public override string ActionType => "CharacterSlot";
        public override bool RequiresWindowHandle => true;

        public CharacterSlotActionExecutor(
            ILoggingService loggingService,
            IUIAutomationService automationService,
            IScreenDetectionCoordinator screenDetection,
            ITemplateMatchingService templateService)
            : base(loggingService, automationService)
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
                _ = _loggingService.LogErrorAsync("[CHARACTER-SLOT] No account context available");
                return false;
            }

            var account = context.QueueItem.Account;
            var targetSlot = account.FFXICharacterSlot;

            if (targetSlot < 1 || targetSlot > 16)
            {
                _ = _loggingService.LogErrorAsync($"[CHARACTER-SLOT] Invalid character slot: {targetSlot} (must be 1-16)");
                return false;
            }

            _ = _loggingService.LogInfoAsync($"[CHARACTER-SLOT] Navigating to character slot {targetSlot} for account {account.DisplayName}");

            try
            {
                // Click-only navigation (requires prior template detection)
                var clicked = await NavigateWithClickAsync(action, context, targetSlot, cancellationToken);
                if (clicked)
                {
                    _ = _loggingService.LogInfoAsync($"[CHARACTER-SLOT] Successfully clicked character slot {targetSlot}");
                    return true;
                }

                _ = _loggingService.LogErrorAsync("[CHARACTER-SLOT] Click navigation failed or not available (missing template match or click points)");
                return false;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync($"[CHARACTER-SLOT] Failed to navigate to character slot {targetSlot}", ex);
                return false;
            }
        }

        private async Task<bool> NavigateWithClickAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            int targetSlot,
            CancellationToken cancellationToken)
        {

            // Get configured click points from action parameters (JSON array)
            var points = action.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new List<RelativeClickOffset>());
            if (points == null || points.Count < 16)
            {
                _ = _loggingService.LogWarningAsync($"[CHARACTER-SLOT] ClickPoints missing or fewer than 16 (found {points?.Count ?? 0}); cannot click");
                return false;
            }
            var clickPoint = points[Math.Clamp(targetSlot - 1, 0, points.Count - 1)];

            // Check if template match is required (only for template-relative clicks)
            if (!clickPoint.FromCenter && context.TemplateMatch == null)
            {
                _ = _loggingService.LogErrorAsync("[CHARACTER-SLOT] Template-relative click requested but no template match available");
                return false;
            }

            // Calculate click point (supports both template-relative and center-relative)
            System.Drawing.Point screenPoint = CalculateClickPoint(clickPoint, context, "CHARACTER-SLOT");

            // Move mouse and click (base class handles post-action delay)
            await _automationService.MoveMouseAsync(screenPoint, cancellationToken);
            await _automationService.ClickWindowRelativeAsync(context.WindowHandle, screenPoint, cancellationToken);

            return true;
        }
    }
}

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
    /// **Navigation Method (MVP):**
    /// - Keyboard only: Use arrow keys to navigate to the target slot
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
    /// - (Optional) TemplatePath: If provided, can be used for future enhancements; ignored in MVP
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

            try
            {
                // MVP: Keyboard-only navigation
                await NavigateWithKeyboardAsync(action, context, targetSlot, cancellationToken);

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

            // Ensure window has focus to receive keyboard input
            if (context.WindowHandle != IntPtr.Zero)
            {
                await _automationService.EnsureWindowFocusAsync(context.WindowHandle, cancellationToken);
                await Task.Delay(100, cancellationToken);
            }

            // DX9-friendly behavior fallback: First Down typically selects slot 1 (when none is selected)
            await _automationService.SendKeyAsync(ConsoleKey.DownArrow, cancellationToken);
            await Task.Delay(action.DelayMs, cancellationToken);

            // Move down to target slot (targetSlot more times)
            var downPresses = Math.Max(0, targetSlot);
            for (int i = 0; i < downPresses; i++)
            {
                await _automationService.SendKeyAsync(ConsoleKey.DownArrow, cancellationToken);
                await Task.Delay(action.DelayMs, cancellationToken);
            }

            // Press Enter to select slot that was navigated to
            await _automationService.SendKeyAsync(ConsoleKey.Enter, cancellationToken);
            await Task.Delay(action.DelayMs, cancellationToken);

            await _loggingService.LogDebugAsync($"[MEMBER-SLOT] Keyboard navigation completed: 1 initial down + {downPresses} down");
        }

        // Click navigation removed in MVP to reduce complexity; keyboard-only is supported
    }
}

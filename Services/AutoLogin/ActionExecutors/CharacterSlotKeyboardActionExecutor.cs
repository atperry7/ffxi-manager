using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes character slot selection using keyboard navigation.
    /// Uses arrow keys to navigate through the 16-slot circular list.
    /// </summary>
    /// <remarks>
    /// **Navigation Method (Keyboard-Only):**
    /// - FFXI character selection always starts at slot 1
    /// - Down arrow: moves to next slot (1→2→...→16→1)
    /// - Up arrow: moves to previous slot (1→16→15→...→2→1)
    /// - Enter: confirms selection
    ///
    /// **Optimal Navigation:**
    /// - Slots 1-8: navigate down (0-7 presses)
    /// - Slot 9: either direction (8 presses)
    /// - Slots 10-16: navigate up (7-1 presses)
    ///
    /// **Requirements:**
    /// - Account context must be available via QueueItem
    /// - Account.FFXICharacterSlot must be set (1-16)
    /// - Window must have focus before navigation
    /// </remarks>
    public class CharacterSlotKeyboardActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IUIAutomationService _automationService;

        public override string ActionType => "CharacterSlotKeyboard";
        public override bool RequiresWindowHandle => true;

        public CharacterSlotKeyboardActionExecutor(
            ILoggingService loggingService,
            IUIAutomationService automationService)
            : base(loggingService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
        }

        protected override async Task<bool> ExecuteActionAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            CancellationToken cancellationToken)
        {
            // Validate context
            if (context.QueueItem?.Account == null)
            {
                _ = _loggingService.LogErrorAsync("[CHARACTER-SLOT-KB] No account context available");
                return false;
            }

            var account = context.QueueItem.Account;
            var targetSlot = account.FFXICharacterSlot;

            if (targetSlot < 1 || targetSlot > 16)
            {
                _ = _loggingService.LogErrorAsync($"[CHARACTER-SLOT-KB] Invalid character slot: {targetSlot} (must be 1-16)");
                return false;
            }

            _ = _loggingService.LogInfoAsync($"[CHARACTER-SLOT-KB] Navigating to character slot {targetSlot} for account {account.DisplayName}");

            try
            {
                // Calculate optimal navigation path
                var (direction, presses) = CalculateNavigation(targetSlot);

                _ = _loggingService.LogDebugAsync($"[CHARACTER-SLOT-KB] Navigation: {presses} {direction} press(es), then Enter");

                // Get delay between key presses (use action's DelayMs or default to 100ms)
                var keyDelay = Math.Max(50, action.DelayMs > 0 ? action.DelayMs : 100);

                // Send arrow key presses
                for (int i = 0; i < presses; i++)
                {
                    await _automationService.SendKeyAsync(direction, cancellationToken);

                    // Delay between arrow presses (not after the last one)
                    if (i < presses - 1)
                    {
                        await Task.Delay(keyDelay, cancellationToken);
                    }
                }

                // Delay before Enter if we sent any arrow keys
                if (presses > 0)
                {
                    await Task.Delay(keyDelay, cancellationToken);
                }

                // Confirm selection with Enter
                await _automationService.SendKeyAsync(ConsoleKey.Enter, cancellationToken);

                _ = _loggingService.LogInfoAsync($"[CHARACTER-SLOT-KB] Successfully selected character slot {targetSlot}");
                return true;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync($"[CHARACTER-SLOT-KB] Failed to navigate to character slot {targetSlot}", ex);
                return false;
            }
        }

        /// <summary>
        /// Calculates the optimal navigation direction and number of key presses.
        /// </summary>
        /// <param name="targetSlot">Target slot (1-16)</param>
        /// <returns>Direction key and number of presses needed</returns>
        private static (ConsoleKey direction, int presses) CalculateNavigation(int targetSlot)
        {
            // Slot 1 is default - no navigation needed
            if (targetSlot == 1)
            {
                return (ConsoleKey.Enter, 0);
            }

            // Calculate presses for each direction
            int downPresses = targetSlot - 1;           // 1→2 = 1, 1→16 = 15
            int upPresses = 17 - targetSlot;            // 1→16 = 1, 1→2 = 15

            // Choose the shorter path
            return downPresses <= upPresses
                ? (ConsoleKey.DownArrow, downPresses)
                : (ConsoleKey.UpArrow, upPresses);
        }
    }
}

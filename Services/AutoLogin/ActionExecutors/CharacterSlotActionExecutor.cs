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
        private readonly IScreenDetectionCoordinator _screenDetection;
        private readonly ITemplateMatchingService _templateService;

        public override string ActionType => "CharacterSlot";
        public override bool RequiresWindowHandle => true;

        public CharacterSlotActionExecutor(
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

            try
            {
                // Ensure window has focus
                if (context.WindowHandle != IntPtr.Zero)
                {
                    await _automationService.EnsureWindowFocusAsync(context.WindowHandle, cancellationToken);
                    await Task.Delay(100, cancellationToken);
                }

                // MVP: Keyboard-only navigation
                await NavigateWithKeyboardAsync(action, context, targetSlot, cancellationToken);

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

            // Ensure window has focus to receive keyboard input
            if (context.WindowHandle != IntPtr.Zero)
            {
                await _automationService.EnsureWindowFocusAsync(context.WindowHandle, cancellationToken);
                await Task.Delay(100, cancellationToken);
            }

            // Character selection defaults focus on slot 1 in DX9; no Home/reset needed

            // Calculate grid position (0-indexed)
            int rowSize = Math.Max(1, action.GetParameter<int>("RowSize", 4));
            int colSize = Math.Max(1, action.GetParameter<int>("ColumnSize", 4));
            int slotIndex = targetSlot - 1; // Convert to 0-idx
            int row = slotIndex / colSize; // rows determined by columns per row
            int column = slotIndex % colSize;

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

            // Confirm selection
            await _automationService.SendKeyAsync(ConsoleKey.Enter, cancellationToken);
            await Task.Delay(action.DelayMs, cancellationToken);

            await _loggingService.LogDebugAsync($"[CHARACTER-SLOT] Keyboard navigation completed: {row} down, {column} right + Enter");
        }

        // Click navigation removed in MVP; keyboard-only supported
    }
}

using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes keyboard input actions (Tab, Enter, Arrow keys, etc.)
    /// Resolution-independent and works across different UI configurations.
    /// </summary>
    /// <remarks>
    /// Handles all standard keyboard inputs for UI navigation:
    /// - Tab, Enter, Escape, Spacebar
    /// - Arrow keys (Up, Down, Left, Right)
    /// - Page navigation (PageUp, PageDown, Home, End)
    ///
    /// Each action can be repeated via the Count property.
    /// </remarks>
    public class KeyboardActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IUIAutomationService _automationService;

        public override string ActionType => "Keyboard";

        public KeyboardActionExecutor(
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
            var key = action.Action; // Key to press (Tab, Enter, etc.)
            var count = Math.Max(1, action.Count); // Repeat count
            var delayMs = Math.Max(50, action.DelayMs); // Delay after each key

            _ = _loggingService.LogDebugAsync($"[KEYBOARD] Executing {key} x{count} (delay: {delayMs}ms)");

            // Execute keyboard input(s)
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await ExecuteSingleKeyAsync(key, cancellationToken);

                // Delay between repeats
                if (i < count - 1)
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
            }

            _ = _loggingService.LogDebugAsync($"[KEYBOARD] Completed {key} x{count}");

            return true;
        }

        /// <summary>
        /// Executes a single keyboard input based on action name
        /// </summary>
        private async Task ExecuteSingleKeyAsync(string key, CancellationToken cancellationToken)
        {
            switch (key.ToLowerInvariant())
            {
                case "tab":
                    await _automationService.SendKeyAsync(ConsoleKey.Tab, cancellationToken);
                    break;

                case "enter":
                case "return":
                    await _automationService.SendKeyAsync(ConsoleKey.Enter, cancellationToken);
                    break;

                case "escape":
                case "esc":
                    await _automationService.SendKeyAsync(ConsoleKey.Escape, cancellationToken);
                    break;

                case "spacebar":
                case "space":
                    await _automationService.SendKeyAsync(ConsoleKey.Spacebar, cancellationToken);
                    break;

                case "downarrow":
                case "down":
                    await _automationService.SendKeyAsync(ConsoleKey.DownArrow, cancellationToken);
                    break;

                case "uparrow":
                case "up":
                    await _automationService.SendKeyAsync(ConsoleKey.UpArrow, cancellationToken);
                    break;

                case "leftarrow":
                case "left":
                    await _automationService.SendKeyAsync(ConsoleKey.LeftArrow, cancellationToken);
                    break;

                case "rightarrow":
                case "right":
                    await _automationService.SendKeyAsync(ConsoleKey.RightArrow, cancellationToken);
                    break;

                case "home":
                    await _automationService.SendKeyAsync(ConsoleKey.Home, cancellationToken);
                    break;

                case "end":
                    await _automationService.SendKeyAsync(ConsoleKey.End, cancellationToken);
                    break;

                case "pageup":
                    await _automationService.SendKeyAsync(ConsoleKey.PageUp, cancellationToken);
                    break;

                case "pagedown":
                    await _automationService.SendKeyAsync(ConsoleKey.PageDown, cancellationToken);
                    break;

                default:
                    _ = _loggingService.LogWarningAsync($"[KEYBOARD] Unknown key action: {key}");
                    return;
            }
        }
    }
}

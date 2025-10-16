using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.Configuration;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.Navigation
{
    /// <summary>
    /// Implementation of FFXI character slot navigation and selection.
    /// Handles arrow key navigation and Enter key selection for character slots.
    /// </summary>
    public class CharacterNavigationService : ICharacterNavigationService
    {
        private readonly ILoggingService _loggingService;
        private readonly IUIAutomationService _automationService;
        private readonly IWindowHandleManagementService _windowHandleService;

        public CharacterNavigationService(
            ILoggingService loggingService,
            IUIAutomationService automationService,
            IWindowHandleManagementService windowHandleService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _windowHandleService = windowHandleService ?? throw new ArgumentNullException(nameof(windowHandleService));
        }

        public bool ValidateCharacterSlot(int characterSlot)
        {
            if (characterSlot < FFXIGameConfiguration.CharacterSlots.MinSlotNumber ||
                characterSlot > FFXIGameConfiguration.CharacterSlots.MaxSlotNumber)
            {
                throw new InvalidOperationException(
                    $"Invalid character slot: {characterSlot}. Must be between " +
                    $"{FFXIGameConfiguration.CharacterSlots.MinSlotNumber} and {FFXIGameConfiguration.CharacterSlots.MaxSlotNumber}");
            }
            return true;
        }

        public async Task NavigateToCharacterSlotAsync(
            AutoLoginSubtask subtask,
            int targetSlot,
            IntPtr windowHandle,
            CancellationToken cancellationToken)
        {
            if (targetSlot <= FFXIGameConfiguration.CharacterSlots.DefaultSlotNumber)
            {
                await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.SlotNavigationEnd,
                    $"Using default character slot {targetSlot}");
                return;
            }

            await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.SlotNavigationStart,
                $"Navigating to character slot {targetSlot}...");

            int stepsNeeded = targetSlot - FFXIGameConfiguration.CharacterSlots.DefaultSlotNumber;
            await _loggingService.LogInfoAsync($"[NAVIGATION] Need to navigate from slot 1 to slot {targetSlot} (sending {stepsNeeded} down arrows)");

            // Create programmatic navigation action with dynamic count
            var slotNavigation = new NavigationAction
            {
                PostNavigationDelayMs = (int)FFXIGameConfiguration.Delays.NavigationStep.TotalMilliseconds
            };
            slotNavigation.Sequence.Add(new KeyboardAction
            {
                Action = "DownArrow",
                Count = stepsNeeded,
                DelayMs = (int)FFXIGameConfiguration.Delays.NavigationStep.TotalMilliseconds,
                Description = $"Navigate to character slot {targetSlot}"
            });

            // Execute navigation using keyboard strategy
            var navSuccess = await ExecuteNavigationActionAsync(
                subtask,
                slotNavigation,
                windowHandle,
                null, // No template match needed for programmatic navigation
                cancellationToken);

            if (!navSuccess)
            {
                throw new InvalidOperationException($"Failed to navigate to character slot {targetSlot}");
            }

            await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.SlotNavigationEnd,
                $"Reached character slot {targetSlot}");
            await _loggingService.LogInfoAsync($"[NAVIGATION] Navigation complete - should now be on slot {targetSlot}");
        }

        public async Task SelectCharacterSlotAsync(
            AutoLoginSubtask subtask,
            int characterSlot,
            IntPtr windowHandle,
            CancellationToken cancellationToken)
        {
            await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.SlotSelecting,
                $"Selecting character slot {characterSlot} (pressing Enter)...");
            await _loggingService.LogInfoAsync($"[NAVIGATION] About to press Enter to select character slot {characterSlot}");

            // Prepare window for selection
            await _windowHandleService.PrepareWindowForNavigationAsync(
                windowHandle,
                FFXIGameConfiguration.Delays.EnterPreparation,
                cancellationToken);

            // Create programmatic navigation action for slot selection
            var selectionAction = new NavigationAction
            {
                PostNavigationDelayMs = (int)FFXIGameConfiguration.Delays.CharacterLoading.TotalMilliseconds
            };
            selectionAction.Sequence.Add(new KeyboardAction
            {
                Action = "Enter",
                Count = 1,
                DelayMs = 100,
                Description = $"Select character slot {characterSlot}"
            });

            // Execute navigation using keyboard strategy
            var navSuccess = await ExecuteNavigationActionAsync(
                subtask,
                selectionAction,
                windowHandle,
                null, // No template match needed for programmatic navigation
                cancellationToken);

            if (!navSuccess)
            {
                throw new InvalidOperationException($"Failed to select character slot {characterSlot}");
            }

            await _loggingService.LogInfoAsync($"[NAVIGATION] Character slot {characterSlot} selection completed");
            await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.SlotComplete,
                $"Character slot {characterSlot} selected successfully");
        }

        /// <summary>
        /// Executes a navigation action using the UI automation service.
        /// Processes the action sequence (keyboard and/or click actions) in order.
        /// </summary>
        private async Task<bool> ExecuteNavigationActionAsync(
            AutoLoginSubtask subtask,
            NavigationAction action,
            IntPtr windowHandle,
            TemplateMatchResult? templateMatch,
            CancellationToken cancellationToken)
        {
            try
            {
                // Ensure window focus before keyboard input
                await _windowHandleService.PrepareWindowForNavigationAsync(
                    windowHandle,
                    FFXIGameConfiguration.Delays.WindowFocus,
                    cancellationToken);

                // Execute keyboard sequence
                foreach (var keyAction in action.Sequence)
                {
                    await _loggingService.LogDebugAsync($"[NAV_ACTION] {keyAction.Description ?? keyAction.Action} (Count: {keyAction.Count})");

                    // Parse the action string to ConsoleKey
                    var consoleKey = ParseConsoleKey(keyAction.Action);
                    if (consoleKey == null)
                    {
                        await _loggingService.LogWarningAsync($"Unsupported keyboard action: {keyAction.Action}");
                        continue;
                    }

                    for (int i = 0; i < keyAction.Count; i++)
                    {
                        // Send the key using the automation service (ConsoleKey first, IntPtr second)
                        await _automationService.SendKeyAsync(consoleKey.Value, windowHandle, cancellationToken);

                        // Apply delay between repeated keys (if not the last iteration)
                        if (i < keyAction.Count - 1 && keyAction.DelayMs > 0)
                        {
                            await Task.Delay(keyAction.DelayMs, cancellationToken);
                        }
                    }
                }

                // Apply post-navigation delay if configured
                if (action.PostNavigationDelayMs > 0)
                {
                    await Task.Delay(action.PostNavigationDelayMs, cancellationToken);
                }

                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Navigation action failed: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Parses a string action name into a ConsoleKey value.
        /// Supports common navigation keys used in character slot navigation.
        /// </summary>
        private ConsoleKey? ParseConsoleKey(string action)
        {
            return action?.ToUpperInvariant() switch
            {
                "ENTER" or "RETURN" => ConsoleKey.Enter,
                "TAB" => ConsoleKey.Tab,
                "DOWNARROW" or "DOWN" => ConsoleKey.DownArrow,
                "UPARROW" or "UP" => ConsoleKey.UpArrow,
                "LEFTARROW" or "LEFT" => ConsoleKey.LeftArrow,
                "RIGHTARROW" or "RIGHT" => ConsoleKey.RightArrow,
                "ESCAPE" or "ESC" => ConsoleKey.Escape,
                "SPACEBAR" or "SPACE" => ConsoleKey.Spacebar,
                _ => null
            };
        }

        /// <summary>
        /// Updates subtask progress (simple wrapper for progress reporting).
        /// </summary>
        private async Task UpdateProgressAsync(AutoLoginSubtask subtask, int progressValue, string message)
        {
            subtask.UpdateProgress(progressValue, message);
            await Task.CompletedTask;
        }
    }
}

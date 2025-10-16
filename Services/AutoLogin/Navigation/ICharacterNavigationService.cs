using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin.Navigation
{
    /// <summary>
    /// Service for FFXI character slot navigation and selection operations.
    /// Handles arrow key navigation and character slot selection logic.
    /// </summary>
    /// <remarks>
    /// <para><strong>Primary Responsibility:</strong></para>
    /// Provides focused methods for navigating between FFXI character slots using arrow keys
    /// and selecting the target character slot. Encapsulates the slot navigation logic and
    /// programmatic navigation action creation.
    ///
    /// <para><strong>Key Features:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Character slot validation (1-16 slots)</description></item>
    ///   <item><description>Arrow key navigation to target slot from default slot</description></item>
    ///   <item><description>Character slot selection with Enter key</description></item>
    ///   <item><description>Progress reporting during navigation and selection</description></item>
    ///   <item><description>Programmatic navigation action creation</description></item>
    /// </list>
    ///
    /// <para><strong>FFXI Character Slot Behavior:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Character slots numbered 1-16 (configurable via FFXIGameConfiguration)</description></item>
    ///   <item><description>Default slot is slot 1 (already selected when screen appears)</description></item>
    ///   <item><description>Arrow down moves to next slot, arrow up moves to previous slot</description></item>
    ///   <item><description>Navigation wraps around (slot 16 → slot 1 with down arrow)</description></item>
    ///   <item><description>Enter key selects the currently highlighted slot</description></item>
    /// </list>
    ///
    /// <para><strong>Navigation Strategy:</strong></para>
    /// <list type="number">
    ///   <item><description>If target slot is 1 (default), no navigation needed</description></item>
    ///   <item><description>Calculate steps needed: targetSlot - 1 (assuming start at slot 1)</description></item>
    ///   <item><description>Send down arrow key inputs with proper delays between steps</description></item>
    ///   <item><description>Report progress during navigation</description></item>
    ///   <item><description>Prepare window for Enter key (focus + stabilization)</description></item>
    ///   <item><description>Send Enter key to select highlighted slot</description></item>
    /// </list>
    ///
    /// <para><strong>Configuration Dependencies:</strong></para>
    /// <list type="bullet">
    ///   <item><description>FFXIGameConfiguration.CharacterSlots.DefaultSlotNumber - Starting slot (usually 1)</description></item>
    ///   <item><description>FFXIGameConfiguration.CharacterSlots.MinSlotNumber - Minimum valid slot</description></item>
    ///   <item><description>FFXIGameConfiguration.CharacterSlots.MaxSlotNumber - Maximum valid slot</description></item>
    ///   <item><description>FFXIGameConfiguration.Delays.NavigationStep - Delay between arrow key presses</description></item>
    ///   <item><description>FFXIGameConfiguration.Delays.EnterPreparation - Delay before Enter key</description></item>
    ///   <item><description>FFXIGameConfiguration.Delays.CharacterLoading - Delay after selection</description></item>
    ///   <item><description>FFXIGameConfiguration.ProgressMilestones.Slot* - Progress reporting values</description></item>
    /// </list>
    ///
    /// <para><strong>Usage Example:</strong></para>
    /// <code>
    /// // Validate character slot number
    /// var characterSlot = queueItem.Account?.FFXICharacterSlot ?? 1;
    /// _navigationService.ValidateCharacterSlot(characterSlot); // throws if invalid
    ///
    /// // Navigate to character slot (e.g., slot 3)
    /// await _navigationService.NavigateToCharacterSlotAsync(
    ///     subtask,
    ///     3,
    ///     ffxiWindowHandle,
    ///     cancellationToken);
    ///
    /// // Select the character slot
    /// await _navigationService.SelectCharacterSlotAsync(
    ///     subtask,
    ///     3,
    ///     ffxiWindowHandle,
    ///     cancellationToken);
    /// </code>
    /// </remarks>
    public interface ICharacterNavigationService
    {
        /// <summary>
        /// Validates that the character slot number is within acceptable range.
        /// Provides clear error messages for invalid slot configurations.
        /// </summary>
        /// <param name="characterSlot">Character slot number to validate</param>
        /// <returns>True if slot is valid</returns>
        /// <exception cref="InvalidOperationException">Thrown when slot is outside valid range (1-16)</exception>
        /// <remarks>
        /// This method should be called before attempting navigation to ensure the target slot
        /// is valid. Invalid slots will throw an exception with a descriptive error message
        /// indicating the valid slot range.
        /// </remarks>
        bool ValidateCharacterSlot(int characterSlot);

        /// <summary>
        /// Navigates to the specified character slot using arrow key navigation.
        /// Handles the step-by-step navigation with progress reporting and proper delays.
        /// </summary>
        /// <param name="subtask">Subtask for progress updates during navigation</param>
        /// <param name="targetSlot">Target character slot number (1-16)</param>
        /// <param name="windowHandle">FFXI window handle for input delivery</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// <para><strong>Navigation Logic:</strong></para>
        /// <list type="bullet">
        ///   <item><description>If targetSlot == 1 (default), skips navigation (already on correct slot)</description></item>
        ///   <item><description>Calculates steps needed: targetSlot - 1</description></item>
        ///   <item><description>Creates programmatic navigation action with dynamic down arrow count</description></item>
        ///   <item><description>Sends down arrow keys with NavigationStep delay between each</description></item>
        ///   <item><description>Reports progress at NavigationStart and NavigationEnd milestones</description></item>
        /// </list>
        ///
        /// <para><strong>Progress Reporting:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Start: "Navigating to character slot {targetSlot}..."</description></item>
        ///   <item><description>End: "Reached character slot {targetSlot}"</description></item>
        ///   <item><description>Detailed logging of navigation steps for debugging</description></item>
        /// </list>
        /// </remarks>
        /// <exception cref="InvalidOperationException">Navigation action execution fails</exception>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        Task NavigateToCharacterSlotAsync(
            AutoLoginSubtask subtask,
            int targetSlot,
            IntPtr windowHandle,
            CancellationToken cancellationToken);

        /// <summary>
        /// Selects the currently highlighted character slot by pressing Enter.
        /// Includes proper window focus preparation and transition timing.
        /// </summary>
        /// <param name="subtask">Subtask for progress updates during selection</param>
        /// <param name="characterSlot">Character slot number being selected (for logging/progress)</param>
        /// <param name="windowHandle">FFXI window handle for input delivery</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// <para><strong>Selection Steps:</strong></para>
        /// <list type="number">
        ///   <item><description>Reports progress: "Selecting character slot {characterSlot} (pressing Enter)..."</description></item>
        ///   <item><description>Prepares window for Enter key (focus + EnterPreparation delay)</description></item>
        ///   <item><description>Creates programmatic navigation action for Enter key</description></item>
        ///   <item><description>Sends Enter key with CharacterLoading post-navigation delay</description></item>
        ///   <item><description>Reports completion: "Character slot {characterSlot} selected successfully"</description></item>
        /// </list>
        ///
        /// <para><strong>FFXI-Specific Timing:</strong></para>
        /// <list type="bullet">
        ///   <item><description>EnterPreparation delay (750ms): Ensures window focus is stable before critical Enter</description></item>
        ///   <item><description>CharacterLoading delay: Allows character data loading after selection</description></item>
        /// </list>
        /// </remarks>
        /// <exception cref="InvalidOperationException">Selection action execution fails</exception>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        Task SelectCharacterSlotAsync(
            AutoLoginSubtask subtask,
            int characterSlot,
            IntPtr windowHandle,
            CancellationToken cancellationToken);
    }
}

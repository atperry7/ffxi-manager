using System.Collections.Generic;

namespace FFXIManager.Models
{
    /// <summary>
    /// Defines the type of navigation action to perform for UI interaction.
    /// </summary>
    public enum NavigationType
    {
        /// <summary>
        /// Navigation using keyboard input (Tab, Arrow keys, Enter, etc.)
        /// Resolution and DPI independent.
        /// </summary>
        Keyboard,

        /// <summary>
        /// Navigation using mouse clicks with relative positioning.
        /// Coordinates are scaled based on window dimensions and template match location.
        /// </summary>
        RelativeClick,

        /// <summary>
        /// Hybrid approach: Try keyboard first, fallback to relative click if keyboard fails.
        /// Provides best reliability across different configurations.
        /// </summary>
        Hybrid
    }

    /// <summary>
    /// Defines a single keyboard action within a navigation sequence.
    /// </summary>
    public class KeyboardAction
    {
        /// <summary>
        /// The keyboard action to perform (Tab, Enter, DownArrow, UpArrow, etc.)
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Number of times to repeat this action (default: 1)
        /// </summary>
        public int Count { get; set; } = 1;

        /// <summary>
        /// Delay in milliseconds after executing this action (default: 100ms)
        /// </summary>
        public int DelayMs { get; set; } = 100;

        /// <summary>
        /// Optional description of what this action accomplishes
        /// </summary>
        public string? Description { get; set; }
    }

    /// <summary>
    /// Defines relative click coordinates as percentages of the detected template region.
    /// Values range from 0.0 to 1.0, where 0.5 represents the center.
    /// </summary>
    public class RelativeClickOffset
    {
        /// <summary>
        /// Horizontal offset as percentage (0.0 = left edge, 0.5 = center, 1.0 = right edge)
        /// </summary>
        public double X { get; set; } = 0.5;

        /// <summary>
        /// Vertical offset as percentage (0.0 = top edge, 0.5 = center, 1.0 = bottom edge)
        /// </summary>
        public double Y { get; set; } = 0.5;

        /// <summary>
        /// Optional description of what is being clicked
        /// </summary>
        public string? Description { get; set; }
    }

    /// <summary>
    /// Defines a complete navigation action for UI interaction.
    /// Supports keyboard sequences, relative clicking, and hybrid approaches.
    /// </summary>
    public class NavigationAction
    {
        /// <summary>
        /// The type of navigation to perform
        /// </summary>
        public NavigationType Type { get; set; } = NavigationType.Keyboard;

        /// <summary>
        /// Sequence of keyboard actions (used when Type is Keyboard or Hybrid)
        /// </summary>
        public List<KeyboardAction> Sequence { get; set; } = new();

        /// <summary>
        /// Relative click offset (used when Type is RelativeClick or as Hybrid fallback)
        /// </summary>
        public RelativeClickOffset? ClickOffset { get; set; }

        /// <summary>
        /// Optional fallback navigation action (used when primary navigation fails)
        /// Typically used in Hybrid mode: try keyboard, fallback to click
        /// </summary>
        public NavigationAction? Fallback { get; set; }

        /// <summary>
        /// Human-readable description of this navigation action
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Delay in milliseconds after completing the entire navigation sequence
        /// </summary>
        public int PostNavigationDelayMs { get; set; } = 500;
    }
}

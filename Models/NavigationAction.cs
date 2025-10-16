using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FFXIManager.Models
{
    /// <summary>
    /// DEPRECATED: Legacy navigation type enum. No longer used in sequence-based navigation.
    /// The Sequence property now allows mixing keyboard and click actions in any order.
    /// Kept for backward compatibility only.
    /// </summary>
    [Obsolete("NavigationType is deprecated. Use Sequence property to define mixed keyboard/click actions instead.")]
    public enum NavigationType
    {
        /// <summary>
        /// Legacy: Keyboard-only navigation
        /// </summary>
        Keyboard,

        /// <summary>
        /// Legacy: Click-only navigation
        /// </summary>
        RelativeClick,

        /// <summary>
        /// Legacy: Hybrid fallback approach (no longer needed with sequence-based navigation)
        /// </summary>
        Hybrid
    }

    /// <summary>
    /// Defines a single navigation action within a sequence (keyboard or mouse click).
    /// </summary>
    public class KeyboardAction : INotifyPropertyChanged
    {
        private string _action = string.Empty;
        private int _count = 1;
        private int _delayMs = 100;
        private string? _description;
        private double _clickX = 0.5;
        private double _clickY = 0.5;

        /// <summary>
        /// The navigation action to perform (Tab, Enter, Click, etc.)
        /// For keyboard: Tab, Enter, DownArrow, UpArrow, LeftArrow, RightArrow, Escape, Spacebar, Home, End, PageUp, PageDown
        /// For mouse: Click (uses ClickX and ClickY coordinates)
        /// </summary>
        public string Action
        {
            get => _action;
            set { _action = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Number of times to repeat this action (default: 1)
        /// </summary>
        public int Count
        {
            get => _count;
            set { _count = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Delay in milliseconds after executing this action (default: 100ms)
        /// </summary>
        public int DelayMs
        {
            get => _delayMs;
            set { _delayMs = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Optional description of what this action accomplishes
        /// </summary>
        public string? Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Relative X coordinate for Click action (0.0 = left edge, 0.5 = center, 1.0 = right edge)
        /// Only used when Action = "Click"
        /// </summary>
        public double ClickX
        {
            get => _clickX;
            set { _clickX = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Relative Y coordinate for Click action (0.0 = top edge, 0.5 = center, 1.0 = bottom edge)
        /// Only used when Action = "Click"
        /// </summary>
        public double ClickY
        {
            get => _clickY;
            set { _clickY = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
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
    /// Defines a complete navigation action for UI interaction using a sequence-based approach.
    /// Each action in the Sequence can be either keyboard input or mouse clicks, allowing
    /// flexible navigation patterns like: Tab → Click → Tab → Enter.
    /// </summary>
    /// <remarks>
    /// **SEQUENCE-BASED NAVIGATION:**
    /// Navigation is defined as an ordered sequence of actions (KeyboardAction objects).
    /// Each action specifies whether it's a keyboard input (Tab, Enter, etc.) or a Click,
    /// along with parameters like repeat count, delays, and click coordinates.
    ///
    /// This approach replaces the old NavigationType enum pattern and provides much more
    /// flexibility since you can mix keyboard and clicks in any order within a single sequence.
    /// </remarks>
    public class NavigationAction : INotifyPropertyChanged
    {
        private NavigationType _type = NavigationType.Keyboard;
        private int _postNavigationDelayMs = 500;
        private string? _description;

        /// <summary>
        /// DEPRECATED: Legacy navigation type property. No longer used.
        /// Kept for backward compatibility with existing workflow JSON files.
        /// Use the Sequence property to define navigation behavior instead.
        /// </summary>
        [Obsolete("Type property is deprecated. Define navigation using the Sequence property instead.")]
        public NavigationType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// **PRIMARY NAVIGATION DEFINITION**: Ordered sequence of keyboard and click actions.
        /// Mix keyboard actions (Tab, Enter, etc.) with Click actions for flexible navigation.
        ///
        /// Examples:
        /// - [Tab, Tab, Enter] - Pure keyboard navigation
        /// - [Click(0.5, 0.5)] - Single click at center
        /// - [Tab, Click(0.3, 0.7), Enter] - Mixed keyboard and click
        /// </summary>
        public ObservableCollection<KeyboardAction> Sequence { get; set; } = new();

        /// <summary>
        /// Human-readable description of this navigation action
        /// </summary>
        public string? Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Delay in milliseconds after completing the entire navigation sequence
        /// </summary>
        public int PostNavigationDelayMs
        {
            get => _postNavigationDelayMs;
            set { _postNavigationDelayMs = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

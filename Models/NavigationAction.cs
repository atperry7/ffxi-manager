using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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
    /// Defines a complete navigation action for UI interaction.
    /// Supports keyboard sequences, relative clicking, and hybrid approaches.
    /// </summary>
    public class NavigationAction : INotifyPropertyChanged
    {
        private NavigationType _type = NavigationType.Keyboard;
        private int _postNavigationDelayMs = 500;
        private string? _description;

        /// <summary>
        /// The type of navigation to perform
        /// </summary>
        public NavigationType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Sequence of keyboard and click actions to execute in order.
        /// Mix keyboard actions (Tab, Enter, etc.) with Click actions for flexible navigation.
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

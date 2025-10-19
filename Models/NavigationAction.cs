using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FFXIManager.Models
{
    /// <summary>
    /// Defines a single navigation action within a sequence (keyboard, mouse, or workflow action).
    /// Supports both traditional navigation actions (Tab, Click) and workflow-level actions (Launch, Wait).
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
        /// The action to perform. Supports both navigation and workflow actions:
        ///
        /// **Navigation Actions:**
        /// - Keyboard: Tab, Enter, DownArrow, UpArrow, LeftArrow, RightArrow, Escape, Spacebar, Home, End, PageUp, PageDown
        /// - Mouse: Click (uses ClickX and ClickY coordinates)
        ///
        /// **Workflow Actions:**
        /// - Launch: Launch an external application (requires Parameters["ApplicationName"])
        /// - Wait: Pause execution (uses DelayMs)
        /// - Screenshot: Capture a screenshot (optional, for debugging)
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

        /// <summary>
        /// Flexible parameters for action-specific configuration.
        /// Enables extensible action system without modifying the model for each new action type.
        ///
        /// **Launch Action Parameters:**
        /// - ApplicationName (string): Name of external application (must match ExternalApplicationData entry)
        /// - AllowSkipIfRunning (bool): Skip if application is already running (default: false)
        /// - AllowSkipIfNotConfigured (bool): Skip if application not in settings (default: true)
        ///
        /// **Future Actions:**
        /// - Screenshot parameters (path, format, etc.)
        /// - Conditional logic parameters
        /// - Custom validation parameters
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Gets a parameter value with type casting, returning default if not found
        /// </summary>
        public T GetParameter<T>(string key, T defaultValue = default!)
        {
            if (Parameters.TryGetValue(key, out var value))
            {
                try
                {
                    // If caller expects a string, sanitize common UI wrapper artifacts
                    if (typeof(T) == typeof(string))
                    {
                        // Handle values already stored as string (including potential ComboBoxItem ToString)
                        if (value is string s)
                        {
                            return (T)(object)SanitizeParameterString(s);
                        }

                        // Handle WPF ComboBoxItem stored directly
                        var typeName = value?.GetType().FullName ?? string.Empty;
                        if (typeName == "System.Windows.Controls.ComboBoxItem")
                        {
                            var contentProp = value.GetType().GetProperty("Content");
                            var contentVal = contentProp?.GetValue(value)?.ToString() ?? value.ToString() ?? string.Empty;
                            return (T)(object)SanitizeParameterString(contentVal);
                        }
                    }

                    // Handle System.Text.Json.JsonElement from deserialization
                    if (value is System.Text.Json.JsonElement jsonElement)
                    {
                        if (typeof(T) == typeof(string))
                        {
                            var str = jsonElement.GetString();
                            return str != null ? (T)(object)SanitizeParameterString(str) : defaultValue;
                        }
                        else if (typeof(T) == typeof(int))
                            return (T)(object)jsonElement.GetInt32();
                        else if (typeof(T) == typeof(bool))
                            return (T)(object)jsonElement.GetBoolean();
                        else if (typeof(T) == typeof(double))
                            return (T)(object)jsonElement.GetDouble();
                        else
                            return JsonSerializer.Deserialize<T>(jsonElement.GetRawText()) ?? defaultValue;
                    }

                    // Standard type conversion
                    if (value is T typedValue)
                        return typedValue;

                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Sets a parameter value
        /// </summary>
        public void SetParameter(string key, object value)
        {
            object sanitized = value;

            // Normalize WPF ComboBoxItem to its Content string
            var typeName = value?.GetType().FullName ?? string.Empty;
            if (typeName == "System.Windows.Controls.ComboBoxItem")
            {
                var contentProp = value.GetType().GetProperty("Content");
                var contentVal = contentProp?.GetValue(value)?.ToString() ?? value.ToString() ?? string.Empty;
                sanitized = SanitizeParameterString(contentVal);
            }
            else if (value is string s)
            {
                sanitized = SanitizeParameterString(s);
            }

            Parameters[key] = sanitized;
            OnPropertyChanged(nameof(Parameters));
        }

        private static string SanitizeParameterString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;

            const string comboPrefix = "System.Windows.Controls.ComboBoxItem:";
            if (s.StartsWith(comboPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return s.Substring(comboPrefix.Length).Trim();
            }

            return s.Trim();
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
        private int _postNavigationDelayMs = 500;
        private string? _description;

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

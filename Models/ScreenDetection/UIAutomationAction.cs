using System.Drawing;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Represents a UI automation action to be performed
    /// </summary>
    public class UIAutomationAction
    {
        /// <summary>
        /// Type of action to perform
        /// </summary>
        public UIActionType Type { get; set; }

        /// <summary>
        /// Target coordinates for the action (window-relative)
        /// </summary>
        public Point WindowRelativeCoordinates { get; set; }

        /// <summary>
        /// Text input for TypeText actions
        /// </summary>
        public string InputText { get; set; } = string.Empty;

        /// <summary>
        /// Key to press for KeyPress actions
        /// </summary>
        public ConsoleKey Key { get; set; }

        /// <summary>
        /// Modifier keys for KeyPress actions
        /// </summary>
        public ConsoleModifiers Modifiers { get; set; }

        /// <summary>
        /// Delay in milliseconds before executing the action
        /// </summary>
        public int DelayBeforeMs { get; set; } = 0;

        /// <summary>
        /// Delay in milliseconds after executing the action
        /// </summary>
        public int DelayAfterMs { get; set; } = 100;

        /// <summary>
        /// Description of the action for logging
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Whether to verify the action completed successfully
        /// </summary>
        public bool RequiresVerification { get; set; } = false;

        /// <summary>
        /// Maximum number of retry attempts if verification fails
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Template to look for after action completion (for verification)
        /// </summary>
        public string VerificationTemplatePath { get; set; } = string.Empty;

        /// <summary>
        /// Creates a click action
        /// </summary>
        public static UIAutomationAction Click(Point windowRelativeCoords, string description = "")
        {
            return new UIAutomationAction
            {
                Type = UIActionType.Click,
                WindowRelativeCoordinates = windowRelativeCoords,
                Description = description ?? "Click",
                DelayAfterMs = 200
            };
        }

        /// <summary>
        /// Creates a type text action
        /// </summary>
        public static UIAutomationAction TypeText(string text, string description = "")
        {
            return new UIAutomationAction
            {
                Type = UIActionType.TypeText,
                InputText = text,
                Description = description ?? "Type text",
                DelayAfterMs = 100
            };
        }

        /// <summary>
        /// Creates a key press action
        /// </summary>
        public static UIAutomationAction KeyPress(ConsoleKey key, ConsoleModifiers modifiers = 0, string description = "")
        {
            return new UIAutomationAction
            {
                Type = UIActionType.KeyPress,
                Key = key,
                Modifiers = modifiers,
                Description = description ?? $"Press {key}",
                DelayAfterMs = 100
            };
        }

        /// <summary>
        /// Creates a wait action
        /// </summary>
        public static UIAutomationAction Wait(int milliseconds, string description = "")
        {
            return new UIAutomationAction
            {
                Type = UIActionType.Wait,
                DelayAfterMs = milliseconds,
                Description = description ?? $"Wait {milliseconds}ms"
            };
        }

        /// <summary>
        /// Creates a clear and type action
        /// </summary>
        public static UIAutomationAction ClearAndType(string text, string description = "")
        {
            return new UIAutomationAction
            {
                Type = UIActionType.ClearAndType,
                InputText = text,
                Description = description ?? "Clear field and type",
                DelayAfterMs = 200
            };
        }

        /// <summary>
        /// Creates a double-click action
        /// </summary>
        public static UIAutomationAction DoubleClick(Point windowRelativeCoords, string description = "")
        {
            return new UIAutomationAction
            {
                Type = UIActionType.DoubleClick,
                WindowRelativeCoordinates = windowRelativeCoords,
                Description = description ?? "Double-click",
                DelayAfterMs = 200
            };
        }

        /// <summary>
        /// Creates a right-click action
        /// </summary>
        public static UIAutomationAction RightClick(Point windowRelativeCoords, string description = "")
        {
            return new UIAutomationAction
            {
                Type = UIActionType.RightClick,
                WindowRelativeCoordinates = windowRelativeCoords,
                Description = description ?? "Right-click",
                DelayAfterMs = 200
            };
        }

        /// <summary>
        /// Creates a hover action
        /// </summary>
        public static UIAutomationAction Hover(Point windowRelativeCoords, int duration = 500, string description = "")
        {
            return new UIAutomationAction
            {
                Type = UIActionType.Hover,
                WindowRelativeCoordinates = windowRelativeCoords,
                DelayAfterMs = duration,
                Description = description ?? $"Hover for {duration}ms"
            };
        }
    }
}
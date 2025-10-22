using System.Drawing;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Represents a UI element template for screenshot matching
    /// </summary>
    public class UIElementTemplate
    {
        /// <summary>
        /// Unique identifier for the template
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Name of the UI element (e.g., "Launch Button", "Password Field")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Path to the template resource (e.g., "Windower/launch_arrow")
        /// </summary>
        public string TemplatePath { get; set; } = string.Empty;

        /// <summary>
        /// Template image data
        /// </summary>
        public byte[] ImageData { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Width of the template image
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Height of the template image
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Number of channels (3 for BGR, 4 for BGRA)
        /// </summary>
        public int Channels { get; set; } = 3;

        /// <summary>
        /// Click offset from template center (for click actions)
        /// </summary>
        public Point ClickOffset { get; set; } = Point.Empty;

        /// <summary>
        /// Minimum confidence threshold for matching (0.0 to 1.0)
        /// </summary>
        public float ConfidenceThreshold { get; set; } = 0.80f;

        /// <summary>
        /// Tolerance in pixels for position matching
        /// </summary>
        public int PositionTolerance { get; set; } = 5;

        /// <summary>
        /// Type of action to perform when template is matched
        /// </summary>
        public UIActionType ActionType { get; set; } = UIActionType.Click;

        /// <summary>
        /// Application this template belongs to
        /// </summary>
        public string ApplicationName { get; set; } = string.Empty;

        /// <summary>
        /// Version of the template
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// Whether this template supports multi-scale matching
        /// </summary>
        public bool SupportsMultiScale { get; set; } = true;

        /// <summary>
        /// Minimum scale factor for multi-scale matching
        /// </summary>
        public float MinScale { get; set; } = 0.5f;

        /// <summary>
        /// Maximum scale factor for multi-scale matching
        /// </summary>
        public float MaxScale { get; set; } = 2.5f;

        /// <summary>
        /// Additional metadata as JSON
        /// </summary>
        public string Metadata { get; set; } = string.Empty;

        /// <summary>
        /// Gets the center point of the template
        /// </summary>
        public Point Center => new Point(Width / 2, Height / 2);

        /// <summary>
        /// Gets the click point relative to template top-left
        /// </summary>
        public Point GetClickPoint()
        {
            return new Point(
                Center.X + ClickOffset.X,
                Center.Y + ClickOffset.Y
            );
        }

        /// <summary>
        /// Validates that the template data is complete and valid
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(Name) &&
                   !string.IsNullOrEmpty(TemplatePath) &&
                   ImageData.Length > 0 &&
                   Width > 0 &&
                   Height > 0 &&
                   ImageData.Length == Width * Height * Channels;
        }
    }

    /// <summary>
    /// Types of UI automation actions
    /// </summary>
    public enum UIActionType
    {
        /// <summary>
        /// No action
        /// </summary>
        None,

        /// <summary>
        /// Single left click
        /// </summary>
        Click,

        /// <summary>
        /// Double left click
        /// </summary>
        DoubleClick,

        /// <summary>
        /// Right click
        /// </summary>
        RightClick,

        /// <summary>
        /// Type text input
        /// </summary>
        TypeText,

        /// <summary>
        /// Press keyboard key
        /// </summary>
        KeyPress,

        /// <summary>
        /// Wait for a duration
        /// </summary>
        Wait,

        /// <summary>
        /// Mouse hover
        /// </summary>
        Hover,

        /// <summary>
        /// Drag and drop
        /// </summary>
        DragDrop,

        /// <summary>
        /// Clear field before typing
        /// </summary>
        ClearAndType
    }
}

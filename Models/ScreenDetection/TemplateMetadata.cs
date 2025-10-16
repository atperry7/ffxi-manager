using System;
using System.Collections.Generic;
using System.Drawing;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Metadata for a UI element template
    /// </summary>
    public class TemplateMetadata
    {
        /// <summary>
        /// Name of the template
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Path to the template
        /// </summary>
        public string TemplatePath { get; set; } = string.Empty;

        /// <summary>
        /// Type of UI element
        /// </summary>
        public string ElementType { get; set; } = "button";

        /// <summary>
        /// Associated login task step
        /// </summary>
        public string AssociatedStep { get; set; } = string.Empty;

        /// <summary>
        /// Action configuration
        /// </summary>
        public ActionConfig Action { get; set; } = new();

        /// <summary>
        /// Confidence threshold for matching
        /// </summary>
        public float ConfidenceThreshold { get; set; } = 0.80f;

        /// <summary>
        /// Position tolerance in pixels
        /// </summary>
        public int Tolerance { get; set; } = 5;

        /// <summary>
        /// Template version
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// Available variants (different resolutions/scales)
        /// </summary>
        public List<string> Variants { get; set; } = new();

        /// <summary>
        /// Additional properties
        /// </summary>
        public Dictionary<string, object> Properties { get; set; } = new();

        /// <summary>
        /// Navigation action configuration (NEW - preferred over Action for resolution independence)
        /// </summary>
        public NavigationAction? Navigation { get; set; }

        /// <summary>
        /// Action configuration for the template
        /// </summary>
        public class ActionConfig
        {
            /// <summary>
            /// Type of action
            /// </summary>
            public string Type { get; set; } = "click";

            /// <summary>
            /// Click offset from center
            /// </summary>
            public ClickOffset ClickOffset { get; set; } = new();

            /// <summary>
            /// Additional action parameters
            /// </summary>
            public Dictionary<string, object> Parameters { get; set; } = new();
        }

        /// <summary>
        /// Click offset configuration
        /// </summary>
        public class ClickOffset
        {
            /// <summary>
            /// X offset from center
            /// </summary>
            public int X { get; set; } = 0;

            /// <summary>
            /// Y offset from center
            /// </summary>
            public int Y { get; set; } = 0;

            /// <summary>
            /// Convert to Point
            /// </summary>
            public Point ToPoint() => new Point(X, Y);
        }

        /// <summary>
        /// Converts metadata to UIElementTemplate
        /// </summary>
        public UIElementTemplate ToTemplate(byte[] imageData, int width, int height)
        {
            if (!Enum.TryParse<UIActionType>(Action.Type, true, out var actionType))
            {
                actionType = UIActionType.Click;
            }

            return new UIElementTemplate
            {
                Name = Name,
                TemplatePath = TemplatePath,
                ImageData = imageData,
                Width = width,
                Height = height,
                ClickOffset = Action.ClickOffset.ToPoint(),
                ConfidenceThreshold = ConfidenceThreshold,
                PositionTolerance = Tolerance,
                ActionType = actionType,
                Version = Version,
                ApplicationName = GetApplicationFromPath(TemplatePath)
            };
        }

        /// <summary>
        /// Extracts application name from template path
        /// </summary>
        private static string GetApplicationFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            var parts = path.Split('/', '\\');
            return parts.Length > 0 ? parts[0] : string.Empty;
        }
    }
}
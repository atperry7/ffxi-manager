using System.Windows.Media;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents a visual marker for click positions in the Template Navigation Tuner.
    /// Used to visualize where clicks will occur relative to the template image.
    /// </summary>
    public class ClickMarker
    {
        /// <summary>
        /// X position on the canvas (in pixels)
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Y position on the canvas (in pixels)
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// Display label for the marker (e.g., "1", "2", "F" for fallback)
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Description of what this marker represents
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Color of the marker for visual differentiation
        /// </summary>
        public Brush MarkerColor { get; set; } = Brushes.Blue;

        /// <summary>
        /// Index in the navigation sequence (-1 for fallback marker)
        /// </summary>
        public int StepIndex { get; set; }
    }
}

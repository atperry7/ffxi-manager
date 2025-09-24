using System;
using System.Drawing;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Result of a template matching operation
    /// </summary>
    public class TemplateMatchResult
    {
        /// <summary>
        /// The template that was matched
        /// </summary>
        public UIElementTemplate Template { get; set; } = null!;

        /// <summary>
        /// Position of the match in window-relative coordinates (top-left corner)
        /// </summary>
        public Point WindowRelativePosition { get; set; }

        /// <summary>
        /// Size of the matched region
        /// </summary>
        public Size MatchSize { get; set; }

        /// <summary>
        /// Confidence score of the match (0.0 to 1.0)
        /// </summary>
        public float Confidence { get; set; }

        /// <summary>
        /// Scale factor applied during matching
        /// </summary>
        public float Scale { get; set; } = 1.0f;

        /// <summary>
        /// Time taken to find the match in milliseconds
        /// </summary>
        public double MatchTimeMs { get; set; }

        /// <summary>
        /// Timestamp when the match was found
        /// </summary>
        public DateTime MatchTime { get; set; } = DateTime.Now;

        /// <summary>
        /// Whether this match meets the confidence threshold
        /// </summary>
        public bool IsValid => Template != null && Confidence >= Template.ConfidenceThreshold;

        /// <summary>
        /// Gets the center point of the matched region
        /// </summary>
        public Point GetCenterPoint()
        {
            return new Point(
                WindowRelativePosition.X + MatchSize.Width / 2,
                WindowRelativePosition.Y + MatchSize.Height / 2
            );
        }

        /// <summary>
        /// Gets the click point based on template's click offset.
        /// Click offset is relative to the top-left corner of the matched template area.
        /// </summary>
        public Point GetClickPoint()
        {
            if (Template == null) return Point.Empty;

            return new Point(
                WindowRelativePosition.X + Template.ClickOffset.X,
                WindowRelativePosition.Y + Template.ClickOffset.Y
            );
        }

        /// <summary>
        /// Gets the bounding rectangle of the match
        /// </summary>
        public Rectangle GetBoundingRectangle()
        {
            return new Rectangle(WindowRelativePosition, MatchSize);
        }

        /// <summary>
        /// Checks if a point is within the matched region
        /// </summary>
        /// <param name="point">Point to check (window-relative coordinates)</param>
        /// <returns>True if the point is within the matched region</returns>
        public bool ContainsPoint(Point point)
        {
            return GetBoundingRectangle().Contains(point);
        }

        /// <summary>
        /// Creates a failed match result
        /// </summary>
        public static TemplateMatchResult Failed(UIElementTemplate template)
        {
            return new TemplateMatchResult
            {
                Template = template,
                Confidence = 0.0f,
                WindowRelativePosition = Point.Empty,
                MatchSize = Size.Empty
            };
        }

        /// <summary>
        /// Creates a successful match result
        /// </summary>
        public static TemplateMatchResult Success(
            UIElementTemplate template,
            Point position,
            Size size,
            float confidence,
            float scale = 1.0f)
        {
            return new TemplateMatchResult
            {
                Template = template,
                WindowRelativePosition = position,
                MatchSize = size,
                Confidence = confidence,
                Scale = scale,
                MatchTime = DateTime.Now
            };
        }
    }
}
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents a visual marker for click positions in the Template Navigation Tuner.
    /// Used to visualize where clicks will occur relative to the template image.
    /// </summary>
    public class ClickMarker : INotifyPropertyChanged
    {
        private double _x;
        private double _y;
        private double _relativeX;
        private double _relativeY;
        private string _label = string.Empty;
        private string _description = string.Empty;
        private Brush _markerColor = Brushes.Blue;
        private int _stepIndex;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// X position on the canvas (in pixels)
        /// </summary>
        public double X
        {
            get => _x;
            set { if (_x != value) { _x = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Y position on the canvas (in pixels)
        /// </summary>
        public double Y
        {
            get => _y;
            set { if (_y != value) { _y = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Display label for the marker (e.g., "1", "2", "F" for fallback)
        /// </summary>
        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Description of what this marker represents
        /// </summary>
        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Color of the marker for visual differentiation
        /// </summary>
        public Brush MarkerColor
        {
            get => _markerColor;
            set { if (_markerColor != value) { _markerColor = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Index in the navigation sequence (-1 for fallback marker)
        /// </summary>
        public int StepIndex
        {
            get => _stepIndex;
            set { if (_stepIndex != value) { _stepIndex = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Relative X coordinate (template-relative 0.0-1.0 or center-relative -0.5 to 0.5)
        /// </summary>
        public double RelativeX
        {
            get => _relativeX;
            set { if (Math.Abs(_relativeX - value) > 0.0001) { _relativeX = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Relative Y coordinate (template-relative 0.0-1.0 or center-relative -0.5 to 0.5)
        /// </summary>
        public double RelativeY
        {
            get => _relativeY;
            set { if (Math.Abs(_relativeY - value) > 0.0001) { _relativeY = value; OnPropertyChanged(); } }
        }
    }
}

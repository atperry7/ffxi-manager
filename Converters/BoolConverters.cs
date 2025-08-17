using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FFXIManager.Converters
{
    /// <summary>
    /// Converts boolean to FontWeight (Bold for active items, Normal otherwise)
    /// </summary>
    public class BoolToFontWeightConverter : IValueConverter
    {
        public static readonly BoolToFontWeightConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? FontWeights.Bold : FontWeights.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts boolean to color for status highlighting
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public static readonly BoolToColorConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter?.ToString() == "Status")
            {
                // For status column - use theme-aware colors instead of hard-coded colors
                if (value is true)
                {
                    // Active profiles get a success/green color
                    var successBrush = System.Windows.Application.Current.TryFindResource("SuccessBrush") as SolidColorBrush;
                    return successBrush ?? Brushes.DarkGreen;
                }
                else
                {
                    // Non-active profiles use primary text color for good contrast
                    var primaryTextBrush = System.Windows.Application.Current.TryFindResource("PrimaryTextBrush") as SolidColorBrush;
                    return primaryTextBrush ?? Brushes.Black;
                }
            }

            // For other uses - use theme-aware colors
            if (value is true)
            {
                var accentBrush = System.Windows.Application.Current.TryFindResource("AccentBrush") as SolidColorBrush;
                return accentBrush ?? Brushes.DarkBlue;
            }
            else
            {
                var primaryTextBrush = System.Windows.Application.Current.TryFindResource("PrimaryTextBrush") as SolidColorBrush;
                return primaryTextBrush ?? Brushes.Black;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Combines profile type and active status into a clear, user-friendly status message
    /// </summary>
    public class ProfileStatusConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return "Available";

            var isSystemFile = values[0] as bool? ?? false;
            var isCurrentlyActive = values.Length > 2 ? (values[2] as bool? ?? false) : false;

            if (isSystemFile)
            {
                return "System File"; // This is the login_w.bin file
            }
            else if (isCurrentlyActive)
            {
                return "Active"; // This profile is currently loaded in login_w.bin
            }
            else
            {
                return "Available"; // This backup is available for use
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Inverts a boolean value and converts to Visibility
    /// </summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
                return visibility != Visibility.Visible;
            return false;
        }
    }

    /// <summary>
    /// Simple boolean inverter for IsEnabled binding - OneWay only
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return !boolValue;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Throw to prevent two-way binding on read-only properties
            throw new NotSupportedException("InverseBooleanConverter only supports OneWay binding");
        }
    }
}

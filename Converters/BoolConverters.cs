using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FFXIManager.Converters
{
    /// <summary>
    /// Converts boolean to Active/Backup status
    /// </summary>
    public class BoolToActiveConverter : IValueConverter
    {
        public static readonly BoolToActiveConverter Instance = new();
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? "System File" : "Backup";
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// Converts boolean to FontWeight (Bold for active, Normal for inactive)
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
    /// Converts boolean to color (Green for currently active, DarkBlue for system file, Black for inactive)
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public static readonly BoolToColorConverter Instance = new();
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter?.ToString() == "Status")
            {
                // For status column - green for currently active profiles
                return value is true ? Brushes.DarkGreen : Brushes.Black;
            }
            
            // For other uses - green for active/system files, black for others
            return value is true ? Brushes.Green : Brushes.Black;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// Converts IsCurrentlyActive boolean to a visual indicator
    /// </summary>
    public class BoolToCurrentlyActiveConverter : IValueConverter
    {
        public static readonly BoolToCurrentlyActiveConverter Instance = new();
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is true)
            {
                return "?? Currently Active";
            }
            return string.Empty; // Return empty string instead of null for better handling
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// Combines profile type and active status into a meaningful status message
    /// </summary>
    public class ProfileStatusConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values?.Length != 2) return "Unknown";
            
            var isActive = values[0] as bool? ?? false;
            var isCurrentlyActive = values[1] as bool? ?? false;
            
            if (isActive)
            {
                return "System File"; // This is the login_w.bin file
            }
            else if (isCurrentlyActive)
            {
                return "Active Profile"; // This backup is currently in use
            }
            else
            {
                return "Inactive"; // This backup is not currently in use
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
}
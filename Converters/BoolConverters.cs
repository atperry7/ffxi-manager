using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FFXIManager.Converters
{
    /// <summary>
    /// Converts boolean to Active/Backup status - UPDATED FOR SIMPLIFIED VERSION
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
    /// Converts boolean to FontWeight (Bold for system file, Normal for backup)
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
    /// Converts boolean to color - UPDATED FOR ACTIVE PROFILES
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public static readonly BoolToColorConverter Instance = new();
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter?.ToString() == "Status")
            {
                // For status column - green for active profiles, black for others
                return value is true ? Brushes.DarkGreen : Brushes.Black;
            }
            
            // For other uses - blue for system files, black for others
            return value is true ? Brushes.DarkBlue : Brushes.Black;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// Converts IsLastUserChoice boolean to a visual indicator
    /// </summary>
    public class BoolToLastUserChoiceConverter : IValueConverter
    {
        public static readonly BoolToLastUserChoiceConverter Instance = new();
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is true)
            {
                return "Last Choice";
            }
            return string.Empty;
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
    
    /// <summary>
    /// Converter to show/hide active profile indicator
    /// </summary>
    public class ActiveProfileVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // This will be handled by the MainViewModel to compare profile names
            return Visibility.Collapsed; // Default to hidden, MainViewModel will handle the logic
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
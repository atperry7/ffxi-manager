using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FFXIManager.ViewModels.CharacterMonitor
{
    /// <summary>
    /// Value converters used by the Character Monitor UI
    /// </summary>
    public static class CharacterMonitorConverters
    {
        /// <summary>
        /// Converter for greater-than comparisons
        /// </summary>
        public static readonly IValueConverter GreaterThan = new GreaterThanConverter();
        
        /// <summary>
        /// Converter for inverting boolean values to visibility
        /// </summary>
        public static readonly IValueConverter InverseBool = new InverseBooleanToVisibilityConverter();
    }

    /// <summary>
    /// Compares a value to see if it's greater than a parameter
    /// </summary>
    public class GreaterThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            try
            {
                var val = System.Convert.ToDouble(value);
                var param = System.Convert.ToDouble(parameter);
                return val > param;
            }
            catch
            {
                return false;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
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
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }
            return false;
        }
    }
}
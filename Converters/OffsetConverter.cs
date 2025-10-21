using System.Globalization;
using System.Windows.Data;

namespace FFXIManager.Converters
{
    /// <summary>
    /// Converts a double value by adding a specified offset
    /// Used for positioning visual elements with offsets
    /// </summary>
    public class OffsetConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue && parameter is string offsetString)
            {
                if (double.TryParse(offsetString, out var offset))
                {
                    return doubleValue + offset;
                }
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace FFXIManager.Converters
{
    /// <summary>
    /// Adds multiple double values together
    /// Used in MultiBinding scenarios
    /// </summary>
    public class AddConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Length == 0)
                    return 0.0;

                var sum = values
                    .Where(v => v is double)
                    .Cast<double>()
                    .Sum();

                return sum;
            }
            catch
            {
                return 0.0;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

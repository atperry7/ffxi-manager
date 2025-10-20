using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace FFXIManager.Converters
{
    /// <summary>
    /// Multiplies two double values (value0 * value1). If more than two are
    /// supplied, multiplies all double values in the array.
    /// </summary>
    public class MultiplyConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Length == 0)
                    return 0.0;

                double product = 1.0;
                bool any = false;
                foreach (var v in values)
                {
                    if (v is double d && !double.IsNaN(d) && !double.IsInfinity(d))
                    {
                        product *= d;
                        any = true;
                    }
                }
                return any ? product : 0.0;
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


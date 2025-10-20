using System;
using System.Globalization;
using System.Windows.Data;

namespace FFXIManager.Converters
{
    /// <summary>
    /// Converts a relative coordinate (0..1) into a translated pixel position accounting for
    /// Image Stretch=Uniform letterboxing. Expects values:
    /// [0] rel (double 0..1), [1] controlActualWidth, [2] controlActualHeight,
    /// [3] sourcePixelWidth, [4] sourcePixelHeight. Parameter: "X" or "Y".
    /// </summary>
    public class RelativeToUniformImageCoordinateConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                double rel = SafeDouble(values, 0);
                double cw = SafeDouble(values, 1);
                double ch = SafeDouble(values, 2);
                double sw = SafeDouble(values, 3);
                double sh = SafeDouble(values, 4);
                string axis = (parameter as string ?? "X").ToUpperInvariant();

                if (cw <= 0 || ch <= 0 || sw <= 0 || sh <= 0) return 0.0;

                double ar = sw / sh;
                double cwAr = cw / ch;

                double displayW, displayH, offsetX, offsetY;
                if (cwAr > ar)
                {
                    // control is wider than image aspect -> pillarbox left/right
                    displayH = ch;
                    displayW = ar * ch;
                    offsetX = (cw - displayW) / 2.0;
                    offsetY = 0.0;
                }
                else
                {
                    // control is taller than image aspect -> letterbox top/bottom
                    displayW = cw;
                    displayH = cw / ar;
                    offsetX = 0.0;
                    offsetY = (ch - displayH) / 2.0;
                }

                if (axis == "X")
                {
                    return offsetX + rel * displayW;
                }
                else
                {
                    return offsetY + rel * displayH;
                }
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

        private static double SafeDouble(object[] vals, int index)
        {
            if (index >= vals.Length) return 0.0;
            var v = vals[index];
            try
            {
                if (v is double d) return d;
                if (v is float f) return f;
                if (v is int i) return i;
                if (v is long l) return l;
                if (v is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var ds)) return ds;
            }
            catch { }
            return 0.0;
        }
    }
}


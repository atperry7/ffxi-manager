using FFXIManager.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FFXIManager.Converters
{
    public sealed class NotificationTypeToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not NotificationType type)
            {
                return Application.Current.TryFindResource("PrimaryTextBrush") as Brush;
            }

            string key = type switch
            {
                NotificationType.Success => "SuccessBrush",
                NotificationType.Warning => "WarningBrush",
                NotificationType.Error => "DangerBrush",
                _ => "InfoBrush"
            };

            return Application.Current.TryFindResource(key) as Brush ??
                   Application.Current.TryFindResource("PrimaryTextBrush") as Brush ??
                   Brushes.White;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}


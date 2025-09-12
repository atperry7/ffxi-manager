using System;
using System.Globalization;
using System.Windows.Data;
using FFXIManager.Services;

namespace FFXIManager.Converters
{
    public sealed class NotificationTypeToIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is NotificationType type ? type switch
            {
                NotificationType.Success => "✔",
                NotificationType.Warning => "⚠",
                NotificationType.Error => "✖",
                _ => "ⓘ"
            } : "ⓘ";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}


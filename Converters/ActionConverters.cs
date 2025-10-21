using FFXIManager.Models;
using System.Globalization;
using System.Windows.Data;

namespace FFXIManager.Converters
{
    /// <summary>
    /// Converts action type to an icon/emoji for visual representation in action list.
    /// </summary>
    public class ActionIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string action)
                return "?";

            return action.ToLowerInvariant() switch
            {
                "launch" => "▶",
                "tab" => "⇥",
                "enter" => "↵",
                "up" => "↑",
                "down" => "↓",
                "left" => "←",
                "right" => "→",
                "click" => "⊕",
                "wait" => "⏱",
                "escape" => "⎋",
                "space" => "␣",
                _ => "⌨"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a KeyboardAction to a summary string showing key details.
    /// </summary>
    public class ActionSummaryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not KeyboardAction action)
                return string.Empty;

            var actionType = action.Action?.ToLowerInvariant() ?? string.Empty;

            return actionType switch
            {
                "launch" => GetLaunchSummary(action),
                "click" => GetClickSummary(action),
                "wait" => GetWaitSummary(action),
                _ when IsKeyboardAction(actionType) => GetKeyboardSummary(action),
                _ => string.Empty
            };
        }

        private static string GetLaunchSummary(KeyboardAction action)
        {
            var appName = action.GetParameter<string>("ApplicationName", "Not configured");
            var templatePath = action.GetParameter<string>("TemplatePath", string.Empty);
            var hasTemplate = !string.IsNullOrEmpty(templatePath);

            return hasTemplate
                ? $"App: {appName} • Template: {templatePath}"
                : $"App: {appName}";
        }

        private static string GetClickSummary(KeyboardAction action)
        {
            var points = action.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new List<RelativeClickOffset>());
            var n = points?.Count ?? 0;
            if (n <= 0)
            {
                return $"Points: 0 \u0007 Delay: {action.DelayMs}ms";
            }
            return n == 1
                ? $"Points: 1 \u0007 Delay: {action.DelayMs}ms"
                : $"Points: {n} \u0007 Delay: {action.DelayMs}ms";
        }
        private static string GetWaitSummary(KeyboardAction action)
        {
            return $"Duration: {action.DelayMs}ms ({action.DelayMs / 1000.0:F1}s)";
        }

        private static string GetKeyboardSummary(KeyboardAction action)
        {
            var countText = action.Count > 1 ? $" × {action.Count}" : "";
            return $"Key: {action.Action}{countText} • Delay: {action.DelayMs}ms";
        }

        private static bool IsKeyboardAction(string action)
        {
            return action switch
            {
                "tab" or "enter" or "escape" or
                "up" or "down" or "left" or "right" or
                "space" or "backspace" or "delete" or
                "home" or "end" or "pageup" or "pagedown" => true,
                _ => false
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


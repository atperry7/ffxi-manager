using System.Windows;
using System.Windows.Controls;
using FFXIManager.Models;

namespace FFXIManager.Views.WorkflowEditor
{
    /// <summary>
    /// Template selector that routes KeyboardActions to the appropriate action editor UserControl.
    /// Determines which editor to display based on the Action property.
    /// </summary>
    public class ActionEditorTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? LaunchTemplate { get; set; }
        public DataTemplate? KeyboardTemplate { get; set; }
        public DataTemplate? ClickTemplate { get; set; }
        public DataTemplate? WaitTemplate { get; set; }
        public DataTemplate? DefaultTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is not KeyboardAction action)
                return DefaultTemplate ?? base.SelectTemplate(item, container);

            var actionType = action.Action?.ToLowerInvariant() ?? string.Empty;

            return actionType switch
            {
                "launch" => LaunchTemplate,
                "click" => ClickTemplate,
                "wait" => WaitTemplate,
                _ when IsKeyboardAction(actionType) => KeyboardTemplate,
                _ => DefaultTemplate ?? base.SelectTemplate(item, container)
            };
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
    }
}

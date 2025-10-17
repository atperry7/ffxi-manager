using System.Windows;
using System.Windows.Controls;
using FFXIManager.Models;

namespace FFXIManager.Views.WorkflowEditor
{
    /// <summary>
    /// UserControl that displays a summary list of navigation actions in a workflow step.
    /// Provides master list for master-detail action editing pattern.
    /// </summary>
    public partial class ActionListPanel : UserControl
    {
        public ActionListPanel()
        {
            InitializeComponent();
        }

        private void AddAction_Click(object sender, RoutedEventArgs e)
        {
            // Show action type selection dialog
            var menu = new ContextMenu();

            // Launch action
            var launchItem = new MenuItem { Header = "Launch Application" };
            launchItem.Click += (s, args) => AddActionOfType("Launch");
            menu.Items.Add(launchItem);

            // Keyboard actions
            var keyboardMenu = new MenuItem { Header = "Keyboard Input" };
            AddKeyboardMenuItem(keyboardMenu, "Tab");
            AddKeyboardMenuItem(keyboardMenu, "Enter");
            AddKeyboardMenuItem(keyboardMenu, "Arrow Up", "Up");
            AddKeyboardMenuItem(keyboardMenu, "Arrow Down", "Down");
            AddKeyboardMenuItem(keyboardMenu, "Arrow Left", "Left");
            AddKeyboardMenuItem(keyboardMenu, "Arrow Right", "Right");
            AddKeyboardMenuItem(keyboardMenu, "Escape");
            menu.Items.Add(keyboardMenu);

            // Click action
            var clickItem = new MenuItem { Header = "Mouse Click" };
            clickItem.Click += (s, args) => AddActionOfType("Click");
            menu.Items.Add(clickItem);

            // Wait action
            var waitItem = new MenuItem { Header = "Wait / Delay" };
            waitItem.Click += (s, args) => AddActionOfType("Wait");
            menu.Items.Add(waitItem);

            menu.PlacementTarget = sender as Button;
            menu.IsOpen = true;
        }

        private void AddKeyboardMenuItem(MenuItem parent, string displayName, string? actionName = null)
        {
            var item = new MenuItem { Header = displayName };
            var actualActionName = actionName ?? displayName;
            item.Click += (s, args) => AddActionOfType(actualActionName);
            parent.Items.Add(item);
        }

        private void AddActionOfType(string actionType)
        {
            // Get the DataContext (should be the ViewModel or WorkflowStepDefinition)
            if (DataContext is null)
                return;

            // Create new action with sensible defaults
            var newAction = new KeyboardAction
            {
                Action = actionType,
                Description = $"{actionType} action",
                Count = actionType.ToLowerInvariant() == "launch" ? 0 : 1,
                DelayMs = GetDefaultDelay(actionType)
            };

            // Add default parameters for Launch actions
            if (actionType.ToLowerInvariant() == "launch")
            {
                newAction.Parameters["ApplicationName"] = string.Empty;
                newAction.Parameters["AllowSkipIfNotConfigured"] = false;
                newAction.Parameters["AllowSkipIfRunning"] = false;
                newAction.Parameters["RetryAttempts"] = 20;
                newAction.Parameters["RetryDelayMs"] = 500;
            }

            // Raise event or command to add the action
            var args = new ActionAddedEventArgs { NewAction = newAction };
            RaiseEvent(new RoutedEventArgs(ActionAddedEvent, args));
        }

        private static int GetDefaultDelay(string actionType)
        {
            return actionType.ToLowerInvariant() switch
            {
                "launch" => 0,
                "tab" or "up" or "down" or "left" or "right" => 200,
                "enter" => 500,
                "click" => 500,
                "wait" => 1000,
                _ => 200
            };
        }

        private void DeleteAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is KeyboardAction action)
            {
                var args = new ActionDeletedEventArgs { DeletedAction = action };
                RaiseEvent(new RoutedEventArgs(ActionDeletedEvent, args));
            }
        }

        // Routed events for add/delete actions
        public static readonly RoutedEvent ActionAddedEvent = EventManager.RegisterRoutedEvent(
            "ActionAdded", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ActionListPanel));

        public static readonly RoutedEvent ActionDeletedEvent = EventManager.RegisterRoutedEvent(
            "ActionDeleted", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ActionListPanel));

        public event RoutedEventHandler ActionAdded
        {
            add { AddHandler(ActionAddedEvent, value); }
            remove { RemoveHandler(ActionAddedEvent, value); }
        }

        public event RoutedEventHandler ActionDeleted
        {
            add { AddHandler(ActionDeletedEvent, value); }
            remove { RemoveHandler(ActionDeletedEvent, value); }
        }
    }

    public class ActionAddedEventArgs : RoutedEventArgs
    {
        public KeyboardAction? NewAction { get; set; }
    }

    public class ActionDeletedEventArgs : RoutedEventArgs
    {
        public KeyboardAction? DeletedAction { get; set; }
    }
}

using System.Windows;
using FFXIManager.Models.Settings;

namespace FFXIManager.Views
{
    /// <summary>
    /// Dialog for editing individual keyboard shortcuts.
    /// </summary>
    public partial class HotkeyEditDialog : Window
    {
        private KeyboardShortcutConfig _originalShortcut;

        /// <summary>
        /// Gets the edited shortcut after dialog completion.
        /// </summary>
        public KeyboardShortcutConfig? EditedShortcut { get; private set; }

        /// <summary>
        /// Initializes a new instance of the HotkeyEditDialog.
        /// </summary>
        /// <param name="shortcut">The shortcut to edit.</param>
        public HotkeyEditDialog(KeyboardShortcutConfig shortcut)
        {
            InitializeComponent();

            _originalShortcut = shortcut;
            DataContext = shortcut;

            // Initialize the key recorder with the current shortcut (keyboard + controller)
            KeyRecorder.SetShortcut(shortcut.Modifiers, shortcut.Key, shortcut.ControllerButton);
            
            // Initialize EditedShortcut with the current values in case user doesn't record a new one
            EditedShortcut = new KeyboardShortcutConfig(
                shortcut.SlotIndex,
                shortcut.Modifiers,
                shortcut.Key,
                shortcut.ControllerButton)
            {
                IsEnabled = shortcut.IsEnabled
            };
        }

        private void KeyRecorder_ShortcutRecorded(object sender, KeyboardShortcutConfig e)
        {
            // Update the edited shortcut when a new one is recorded
            EditedShortcut = new KeyboardShortcutConfig(
                _originalShortcut.SlotIndex,
                e.Modifiers,
                e.Key,
                e.ControllerButton)
            {
                IsEnabled = _originalShortcut.IsEnabled
            };

            // No auto-close or confirmation - let the user review and click Apply when ready
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            // Only close with success if we have a valid shortcut recorded
            if (EditedShortcut != null)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please record a keyboard shortcut before applying.",
                    "No Shortcut Recorded", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}

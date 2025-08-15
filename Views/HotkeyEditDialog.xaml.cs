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
            
            // Show current shortcut in header
            UpdateCurrentShortcutDisplay();
            
            // Initialize the key recorder with the current shortcut
            KeyRecorder.SetShortcut(shortcut.Modifiers, shortcut.Key);
        }
        
        /// <summary>
        /// Updates the display of the current shortcut in the header.
        /// </summary>
        private void UpdateCurrentShortcutDisplay()
        {
            var currentText = "Current: ";
            if (_originalShortcut.Key == System.Windows.Input.Key.None)
            {
                currentText += "None (not configured)";
            }
            else
            {
                currentText += _originalShortcut.DisplayText;
            }
            CurrentShortcutText.Text = currentText;
        }

        private void KeyRecorder_ShortcutRecorded(object sender, KeyboardShortcutConfig e)
        {
            // Update the edited shortcut when a new one is recorded
            EditedShortcut = new KeyboardShortcutConfig(
                _originalShortcut.HotkeyId,
                e.Modifiers,
                e.Key)
            {
                SlotIndex = _originalShortcut.SlotIndex,
                IsEnabled = _originalShortcut.IsEnabled
            };
            
            // Show confirmation message with the captured shortcut
            var result = MessageBox.Show(
                $"Successfully captured: {EditedShortcut.DisplayText}\n\nApply this shortcut for {_originalShortcut.SlotDisplayText}?", 
                "Shortcut Captured", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                DialogResult = true;
                Close();
            }
            // If No, just leave the dialog open so user can try again
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

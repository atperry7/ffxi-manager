using System.Windows;
using System.Windows.Controls;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    /// <summary>
    /// Interaction logic for PlayOnlineMemberAccountEditDialog.xaml
    /// </summary>
    public partial class PlayOnlineMemberAccountEditDialog : Window
    {
        private PlayOnlineMemberAccountEditViewModel? ViewModel => DataContext as PlayOnlineMemberAccountEditViewModel;
        private bool _isUserTyping = false;

        /// <summary>
        /// Gets the entered password (for secure storage via Windows Credentials)
        /// </summary>
        public string EnteredPassword => PasswordBox.Password;

        /// <summary>
        /// Gets whether a password change was requested (password entered or account has existing password)
        /// </summary>
        public bool HasPasswordUpdate => !string.IsNullOrWhiteSpace(PasswordBox.Password) || (ViewModel?.Account?.HasStoredPassword == true);

        public PlayOnlineMemberAccountEditDialog()
        {
            InitializeComponent();
            Loaded += PlayOnlineMemberAccountEditDialog_Loaded;
        }

        private void PlayOnlineMemberAccountEditDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // Clear password box for security - passwords will be managed separately
            PasswordBox.Password = string.Empty;

            // Set initial password field border color based on stored password status
            UpdatePasswordFieldColor();

            // Focus the first input
            if (string.IsNullOrEmpty(AccountNameTextBox.Text))
            {
                AccountNameTextBox.Focus();
            }
            else
            {
                POLSlotComboBox.Focus();
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            // Mark as typing when user changes password
            _isUserTyping = !string.IsNullOrEmpty(PasswordBox.Password);
            UpdatePasswordFieldColor();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.Account == null)
            {
                DialogResult = false;
                return;
            }

            // Validate required fields
            if (ViewModel.Account.POLMemberSlot < 1 || ViewModel.Account.POLMemberSlot > 4)
            {
                MessageBox.Show(
                    "POL Member Slot must be between 1 and 4.",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                POLSlotComboBox.Focus();
                return;
            }

            if (ViewModel.Account.FFXICharacterSlot < 1 || ViewModel.Account.FFXICharacterSlot > 16)
            {
                MessageBox.Show(
                    "FFXI Character Slot must be between 1 and 16.",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                FFXISlotComboBox.Focus();
                return;
            }

            // Check for duplicate POL slot (only for new accounts or when slot changed)
            if (!ViewModel.IsEditMode || HasSlotChanged())
            {
                if (!ViewModel.IsPOLSlotAvailable(ViewModel.Account.POLMemberSlot))
                {
                    MessageBox.Show(
                        $"POL Member Slot {ViewModel.Account.POLMemberSlot} is already in use by another account.",
                        "Validation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    POLSlotComboBox.Focus();
                    return;
                }
            }

            // Handle password validation
            bool hasNewPassword = !string.IsNullOrWhiteSpace(PasswordBox.Password);
            bool hasExistingPassword = ViewModel?.Account?.HasStoredPassword == true;

            if (hasNewPassword)
            {
                // New password will be stored through Windows Credentials service
                // This will be handled by the calling ViewModel
            }
            else if (!hasExistingPassword)
            {
                // No password entered and no existing password stored
                var result = MessageBox.Show(
                    "You haven't entered a password. Auto-login functionality will not work without a password.\n\nDo you want to continue without a password?",
                    "Missing Password",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    PasswordBox.Focus();
                    return;
                }
            }
            // If hasExistingPassword is true and no new password entered, keep existing password

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private bool HasSlotChanged()
        {
            // For new accounts, always consider slot as "changed"
            if (!ViewModel?.IsEditMode ?? true)
                return true;

            // For existing accounts, we'd need to track the original slot
            // For now, we'll just return true to be safe
            return true;
        }

        private void UpdatePasswordFieldColor()
        {
            if (ViewModel?.Account == null) return;

            if (_isUserTyping)
            {
                // Orange: User is typing (updating password)
                PasswordBox.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)); // Orange
            }
            else if (ViewModel.Account.HasStoredPassword)
            {
                // Green: Password already stored securely
                PasswordBox.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green
            }
            else
            {
                // Red: No password stored
                PasswordBox.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // Red
            }

            // Set border thickness to make the color more visible
            PasswordBox.BorderThickness = new System.Windows.Thickness(2);
        }
    }
}
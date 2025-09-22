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

        public PlayOnlineMemberAccountEditDialog()
        {
            InitializeComponent();
            Loaded += PlayOnlineMemberAccountEditDialog_Loaded;
        }

        private void PlayOnlineMemberAccountEditDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // Set the password box value if we're editing an existing account
            if (ViewModel?.Account != null && !string.IsNullOrEmpty(ViewModel.Account.POLPassword))
            {
                PasswordBox.Password = ViewModel.Account.POLPassword;
            }

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
            if (ViewModel?.Account != null && sender is PasswordBox passwordBox)
            {
                ViewModel.Account.POLPassword = passwordBox.Password;
            }
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

            // Warn about empty password
            if (string.IsNullOrWhiteSpace(ViewModel.Account.POLPassword))
            {
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
    }
}
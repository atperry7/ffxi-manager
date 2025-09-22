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

        /// <summary>
        /// Gets the entered authentication key for OTP (for secure storage via Windows Credentials)
        /// </summary>
        public string EnteredAuthenticationKey => AuthKeyTextBox.Text;

        public PlayOnlineMemberAccountEditDialog()
        {
            InitializeComponent();
            Loaded += PlayOnlineMemberAccountEditDialog_Loaded;
        }

        private void PlayOnlineMemberAccountEditDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // Clear password box for security - passwords will be managed separately
            PasswordBox.Password = string.Empty;

            // Clear authentication key box for security - keys will be managed separately
            AuthKeyTextBox.Text = string.Empty;

            // Set initial field border colors based on stored status
            UpdatePasswordFieldColor();
            UpdateAuthKeyFieldColor();

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

        private void AuthKeyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateAuthKeyFieldColor();
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

            // Handle OTP validation if enabled
            if (ViewModel.Account.OTPConfiguration?.IsEnabled == true)
            {
                bool hasNewAuthKey = !string.IsNullOrWhiteSpace(AuthKeyTextBox.Text);
                bool hasExistingAuthKey = ViewModel?.Account?.OTPConfiguration?.HasStoredSecret == true;

                if (hasNewAuthKey)
                {
                    // Validate the authentication key format
                    var normalizedKey = ValidateAuthenticationKey(AuthKeyTextBox.Text);
                    if (normalizedKey == null)
                    {
                        MessageBox.Show(
                            "Invalid authentication key format. Please enter a 32-character Square Enix authentication key (with or without spaces).\n\nExample: OBQV O3CU GA4V A6SN PJGW Q33B I5DF EVKW",
                            "Invalid Authentication Key",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        AuthKeyTextBox.Focus();
                        return;
                    }
                }
                else if (!hasExistingAuthKey)
                {
                    // OTP is enabled but no authentication key provided
                    var result = MessageBox.Show(
                        "OTP is enabled but no authentication key was provided. OTP functionality will not work without an authentication key.\n\nDo you want to continue without an authentication key?",
                        "Missing Authentication Key",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.No)
                    {
                        AuthKeyTextBox.Focus();
                        return;
                    }
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

        private void UpdateAuthKeyFieldColor()
        {
            if (ViewModel?.Account?.OTPConfiguration == null || !ViewModel.Account.OTPConfiguration.IsEnabled) return;

            if (!string.IsNullOrWhiteSpace(AuthKeyTextBox.Text))
            {
                var normalizedKey = ValidateAuthenticationKey(AuthKeyTextBox.Text);
                if (normalizedKey != null)
                {
                    // Green: Valid key format
                    AuthKeyTextBox.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green
                }
                else
                {
                    // Red: Invalid key format
                    AuthKeyTextBox.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // Red
                }
            }
            else if (ViewModel.Account.OTPConfiguration.HasStoredSecret)
            {
                // Blue: No input but has stored secret
                AuthKeyTextBox.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)); // Blue
            }
            else
            {
                // Orange: No input and no stored secret
                AuthKeyTextBox.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)); // Orange
            }

            // Set border thickness to make the color more visible
            AuthKeyTextBox.BorderThickness = new System.Windows.Thickness(2);
        }

        private string? ValidateAuthenticationKey(string authenticationKey)
        {
            if (string.IsNullOrWhiteSpace(authenticationKey))
                return null;

            // Remove all spaces and convert to uppercase
            var normalized = authenticationKey.Replace(" ", "").ToUpperInvariant();

            // Square Enix authentication keys are typically 32 characters (Base32)
            if (normalized.Length != 32)
                return null;

            // Validate Base32 format (A-Z, 2-7)
            foreach (char c in normalized)
            {
                if (!((c >= 'A' && c <= 'Z') || (c >= '2' && c <= '7')))
                    return null;
            }

            return normalized;
        }
    }
}
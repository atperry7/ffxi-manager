using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FFXIManager.Models
{
    /// <summary>
    /// Configuration for One-Time Password (OTP) authentication using Square Enix Security Token
    /// </summary>
    public class OTPConfiguration : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private bool _hasStoredSecret;
        private string _providerName = "Square Enix";

        /// <summary>
        /// Indicates whether OTP is enabled for the account
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// Indicates whether an OTP secret key is securely stored in Windows Credential Manager
        /// </summary>
        public bool HasStoredSecret
        {
            get => _hasStoredSecret;
            set => SetProperty(ref _hasStoredSecret, value);
        }

        /// <summary>
        /// OTP Provider name (defaults to Square Enix)
        /// </summary>
        public string ProviderName
        {
            get => _providerName;
            set => SetProperty(ref _providerName, value ?? "Square Enix");
        }

        /// <summary>
        /// Gets whether OTP is properly configured (enabled and has stored secret)
        /// </summary>
        public bool IsConfigured => IsEnabled && HasStoredSecret;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            // Update computed properties when dependencies change
            if (propertyName == nameof(IsEnabled) || propertyName == nameof(HasStoredSecret))
            {
                OnPropertyChanged(nameof(IsConfigured));
            }
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
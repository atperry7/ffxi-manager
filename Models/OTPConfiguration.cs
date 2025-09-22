using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FFXIManager.Models
{
    /// <summary>
    /// Configuration for One-Time Password (OTP) authentication
    /// </summary>
    public class OTPConfiguration : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string? _secretKey;
        private string? _backupCodes;

        /// <summary>
        /// Indicates whether OTP is enabled for the account
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// Secret key for TOTP generation (future implementation)
        /// TODO: Implement secure storage and TOTP generation
        /// </summary>
        public string? SecretKey
        {
            get => _secretKey;
            set => SetProperty(ref _secretKey, value);
        }

        /// <summary>
        /// Backup codes for OTP recovery (future implementation)
        /// </summary>
        public string? BackupCodes
        {
            get => _backupCodes;
            set => SetProperty(ref _backupCodes, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
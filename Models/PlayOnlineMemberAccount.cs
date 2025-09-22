using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents a PlayOnline Member Account associated with a ProfileInfo
    /// </summary>
    public class PlayOnlineMemberAccount : INotifyPropertyChanged
    {
        private Guid _id = Guid.NewGuid();
        private int _polMemberSlot = 1;
        private int _ffxiCharacterSlot = 1;
        private OTPConfiguration? _otpConfiguration;
        private string _polPassword = string.Empty;
        private string _accountName = string.Empty;

        /// <summary>
        /// Unique identifier for this account association
        /// </summary>
        public Guid Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// PlayOnline Member Slot Number (1 to 4)
        /// </summary>
        public int POLMemberSlot
        {
            get => _polMemberSlot;
            set
            {
                if (value < 1 || value > 4)
                    throw new ArgumentOutOfRangeException(nameof(value), "POL Member Slot must be between 1 and 4");
                SetProperty(ref _polMemberSlot, value);
            }
        }

        /// <summary>
        /// FFXI Character Slot Number (1 to 16)
        /// </summary>
        public int FFXICharacterSlot
        {
            get => _ffxiCharacterSlot;
            set
            {
                if (value < 1 || value > 16)
                    throw new ArgumentOutOfRangeException(nameof(value), "FFXI Character Slot must be between 1 and 16");
                SetProperty(ref _ffxiCharacterSlot, value);
            }
        }

        /// <summary>
        /// One-Time Password configuration (optional)
        /// </summary>
        public OTPConfiguration? OTPConfiguration
        {
            get => _otpConfiguration;
            set => SetProperty(ref _otpConfiguration, value);
        }

        /// <summary>
        /// PlayOnline Square Enix Password
        /// TODO: Implement secure storage using Windows Credential Manager
        /// </summary>
        public string POLPassword
        {
            get => _polPassword;
            set => SetProperty(ref _polPassword, value ?? string.Empty);
        }

        /// <summary>
        /// Optional account name/label for user identification
        /// </summary>
        public string AccountName
        {
            get => _accountName;
            set => SetProperty(ref _accountName, value ?? string.Empty);
        }

        /// <summary>
        /// Gets whether OTP is enabled for this account
        /// </summary>
        public bool IsOTPEnabled => OTPConfiguration?.IsEnabled ?? false;

        /// <summary>
        /// Gets a display-friendly description of this account
        /// </summary>
        public string DisplayName
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(AccountName)
                    ? $"Slot {POLMemberSlot}-{FFXICharacterSlot}"
                    : AccountName;
                return IsOTPEnabled ? $"{name} (OTP)" : name;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            // Update DisplayName when relevant properties change
            if (propertyName == nameof(AccountName) ||
                propertyName == nameof(POLMemberSlot) ||
                propertyName == nameof(FFXICharacterSlot) ||
                propertyName == nameof(OTPConfiguration))
            {
                OnPropertyChanged(nameof(DisplayName));
                if (propertyName == nameof(OTPConfiguration))
                {
                    OnPropertyChanged(nameof(IsOTPEnabled));
                }
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
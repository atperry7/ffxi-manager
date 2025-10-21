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
        private string _accountName = string.Empty;
        private Guid? _workflowId;
        private bool _hasStoredPassword;
        private bool _isOTPCodeVisible = false;
        private string? _currentOTPCode;
        private double _otpTimeRemaining = 100.0;

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
        /// Indicates whether a password is stored securely in Windows Credential Manager
        /// </summary>
        public bool HasStoredPassword
        {
            get => _hasStoredPassword;
            set => SetProperty(ref _hasStoredPassword, value);
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
        /// Optional workflow ID for custom login flow (null = use default workflow)
        /// </summary>
        public Guid? WorkflowId
        {
            get => _workflowId;
            set => SetProperty(ref _workflowId, value);
        }

        /// <summary>
        /// Gets whether this account uses a custom workflow (true) or default workflow (false)
        /// </summary>
        public bool HasCustomWorkflow => WorkflowId.HasValue && WorkflowId.Value != Guid.Empty;

        /// <summary>
        /// Gets whether OTP is enabled for this account
        /// </summary>
        public bool IsOTPEnabled => OTPConfiguration?.IsEnabled ?? false;

        /// <summary>
        /// Gets or sets whether the OTP code is currently visible (not masked)
        /// </summary>
        public bool IsOTPCodeVisible
        {
            get => _isOTPCodeVisible;
            set => SetProperty(ref _isOTPCodeVisible, value);
        }

        /// <summary>
        /// Gets or sets the current OTP code for display
        /// </summary>
        public string? CurrentOTPCode
        {
            get => _currentOTPCode;
            set => SetProperty(ref _currentOTPCode, value);
        }

        /// <summary>
        /// Gets or sets the time remaining on the current OTP code (0-100%)
        /// </summary>
        public double OTPTimeRemaining
        {
            get => _otpTimeRemaining;
            set => SetProperty(ref _otpTimeRemaining, value);
        }

        /// <summary>
        /// Gets the display text for OTP code (masked or actual code)
        /// </summary>
        public string OTPCodeDisplay
        {
            get
            {
                if (!IsOTPEnabled)
                    return "N/A";

                if (!OTPConfiguration?.HasStoredSecret == true)
                    return "No Key";

                if (string.IsNullOrEmpty(CurrentOTPCode))
                    return "------";

                return IsOTPCodeVisible ? CurrentOTPCode : "••••••";
            }
        }

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

                var suffix = string.Empty;
                if (IsOTPEnabled && HasStoredPassword)
                    suffix = " (OTP, Secured)";
                else if (IsOTPEnabled)
                    suffix = " (OTP)";
                else if (HasStoredPassword)
                    suffix = " (Secured)";

                return name + suffix;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            // Update computed properties when dependencies change
            if (propertyName == nameof(AccountName) ||
                propertyName == nameof(POLMemberSlot) ||
                propertyName == nameof(FFXICharacterSlot) ||
                propertyName == nameof(OTPConfiguration) ||
                propertyName == nameof(HasStoredPassword))
            {
                OnPropertyChanged(nameof(DisplayName));
                if (propertyName == nameof(OTPConfiguration))
                {
                    OnPropertyChanged(nameof(IsOTPEnabled));
                    OnPropertyChanged(nameof(OTPCodeDisplay));
                }
            }

            // Update OTP display when visibility or code changes
            if (propertyName == nameof(IsOTPCodeVisible) ||
                propertyName == nameof(CurrentOTPCode))
            {
                OnPropertyChanged(nameof(OTPCodeDisplay));
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
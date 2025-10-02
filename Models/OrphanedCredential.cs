using System;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents a credential stored in Windows Credential Manager that is not linked to any current account
    /// </summary>
    public class OrphanedCredential
    {
        /// <summary>
        /// The account GUID extracted from the credential target name
        /// </summary>
        public Guid AccountId { get; set; }

        /// <summary>
        /// Full credential target name for password (if exists)
        /// </summary>
        public string? PasswordTargetName { get; set; }

        /// <summary>
        /// Username stored in the password credential (if exists)
        /// </summary>
        public string? PasswordUsername { get; set; }

        /// <summary>
        /// Full credential target name for OTP secret (if exists)
        /// </summary>
        public string? OtpTargetName { get; set; }

        /// <summary>
        /// Indicates whether a password credential exists for this account
        /// </summary>
        public bool HasPassword => !string.IsNullOrEmpty(PasswordTargetName);

        /// <summary>
        /// Indicates whether an OTP credential exists for this account
        /// </summary>
        public bool HasOtp => !string.IsNullOrEmpty(OtpTargetName);

        /// <summary>
        /// Short display identifier (last 8 characters of GUID)
        /// </summary>
        public string ShortId => AccountId.ToString("N")[^8..].ToUpper();

        /// <summary>
        /// Display-friendly credential type description
        /// </summary>
        public string CredentialTypes
        {
            get
            {
                if (HasPassword && HasOtp)
                    return "Password + OTP";
                if (HasPassword)
                    return "Password Only";
                if (HasOtp)
                    return "OTP Only";
                return "Unknown";
            }
        }
    }
}

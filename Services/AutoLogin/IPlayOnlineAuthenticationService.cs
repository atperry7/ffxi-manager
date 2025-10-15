using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Interface for PlayOnline authentication service.
    /// Provides operations for password and OTP credential management.
    /// </summary>
    public interface IPlayOnlineAuthenticationService
    {
        /// <summary>
        /// Validates that the account has a stored password configured.
        /// </summary>
        /// <param name="account">PlayOnline member account to validate</param>
        /// <param name="profileFilePath">Profile file path for credential target generation</param>
        /// <returns>True if password is stored, false otherwise</returns>
        Task<bool> ValidatePasswordConfigurationAsync(PlayOnlineMemberAccount account, string profileFilePath);

        /// <summary>
        /// Retrieves the stored password for a PlayOnline account with comprehensive validation.
        /// </summary>
        /// <param name="account">PlayOnline member account</param>
        /// <param name="profileFilePath">Profile file path for credential target generation</param>
        /// <returns>The retrieved password, or null if not found</returns>
        Task<string?> RetrieveSecurePasswordAsync(PlayOnlineMemberAccount account, string profileFilePath);

        /// <summary>
        /// Validates whether OTP is required for the specified account.
        /// </summary>
        /// <param name="account">PlayOnline member account to check</param>
        /// <returns>True if OTP is required, false otherwise</returns>
        Task<bool> IsOTPRequiredAsync(PlayOnlineMemberAccount account);

        /// <summary>
        /// Generates a secure OTP code for the specified account with comprehensive validation.
        /// </summary>
        /// <param name="account">PlayOnline member account</param>
        /// <param name="profileFilePath">Profile file path for credential target generation</param>
        /// <returns>The generated OTP code, or null if generation fails</returns>
        Task<string?> GenerateSecureOTPCodeAsync(PlayOnlineMemberAccount account, string profileFilePath);

        /// <summary>
        /// Validates that OTP is properly configured for the specified account.
        /// Checks both OTP enablement and stored secret availability.
        /// </summary>
        /// <param name="account">PlayOnline member account to validate</param>
        /// <param name="profileFilePath">Profile file path for credential target generation</param>
        /// <returns>True if OTP is properly configured, false otherwise</returns>
        Task<bool> ValidateOTPConfigurationAsync(PlayOnlineMemberAccount account, string profileFilePath);

        /// <summary>
        /// Validates that the account name is not empty.
        /// </summary>
        /// <param name="accountName">Account name to validate</param>
        /// <param name="propertyDescription">Description of the property for error messages</param>
        /// <returns>True if valid, false otherwise</returns>
        Task<bool> ValidateAccountPropertyAsync(string? accountName, string propertyDescription);
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Service responsible for PlayOnline authentication operations.
    /// Provides a unified interface for password and OTP credential management
    /// with comprehensive validation and error handling.
    ///
    /// This service wraps:
    /// - IWindowsCredentialsService: For secure password storage/retrieval
    /// - IOTPService: For OTP code generation
    /// - ILoggingService: For diagnostic logging
    ///
    /// All operations include security-aware logging that masks sensitive data
    /// while providing sufficient diagnostic information for troubleshooting.
    /// </summary>
    public class PlayOnlineAuthenticationService : IPlayOnlineAuthenticationService
    {
        private readonly IWindowsCredentialsService _credentialsService;
        private readonly IOTPService _otpService;
        private readonly ILoggingService _loggingService;

        public PlayOnlineAuthenticationService(
            IWindowsCredentialsService credentialsService,
            IOTPService otpService,
            ILoggingService loggingService)
        {
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
            _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        /// <summary>
        /// Validates that the account has a stored password configured.
        /// </summary>
        /// <param name="account">PlayOnline member account to validate</param>
        /// <param name="profileFilePath">Profile file path for credential target generation</param>
        /// <returns>True if password is stored, false otherwise</returns>
        /// <exception cref="ArgumentNullException">Thrown if account is null</exception>
        public async Task<bool> ValidatePasswordConfigurationAsync(
            PlayOnlineMemberAccount account,
            string profileFilePath)
        {
            if (account == null)
                throw new ArgumentNullException(nameof(account));

            if (!account.HasStoredPassword)
            {
                await _loggingService.LogWarningAsync($"No stored password found for account: {account.AccountName}");
                return false;
            }

            // Verify the password actually exists in credential manager
            var credentialTarget = _credentialsService.GenerateCredentialTarget(profileFilePath, account.Id);
            var exists = await _credentialsService.CredentialExistsAsync(credentialTarget, account.AccountName);

            if (!exists)
            {
                await _loggingService.LogWarningAsync($"Password marked as stored but not found in credential manager for account: {account.AccountName}");
                return false;
            }

            await _loggingService.LogDebugAsync($"Password configuration validated for account: {account.AccountName}");
            return true;
        }

        /// <summary>
        /// Retrieves the stored password for a PlayOnline account with comprehensive validation.
        /// </summary>
        /// <param name="account">PlayOnline member account</param>
        /// <param name="profileFilePath">Profile file path for credential target generation</param>
        /// <returns>The retrieved password, or null if not found</returns>
        /// <exception cref="ArgumentNullException">Thrown if account is null</exception>
        /// <remarks>
        /// This method provides comprehensive diagnostic logging without exposing sensitive data:
        /// - Logs password length for validation
        /// - Identifies potentially problematic characters
        /// - Masks actual password content in logs
        /// </remarks>
        public async Task<string?> RetrieveSecurePasswordAsync(
            PlayOnlineMemberAccount account,
            string profileFilePath)
        {
            if (account == null)
                throw new ArgumentNullException(nameof(account));

            var credentialTarget = _credentialsService.GenerateCredentialTarget(profileFilePath, account.Id);
            var password = await _credentialsService.RetrievePasswordAsync(credentialTarget, account.AccountName);

            if (string.IsNullOrEmpty(password))
            {
                await _loggingService.LogWarningAsync($"Could not retrieve password for account: {account.AccountName}");
                return null;
            }

            // SECURITY: Log password characteristics without exposing content
            await _loggingService.LogDebugAsync($"Password retrieved for {account.AccountName}: Length={password.Length} characters");

            // DIAGNOSTIC: Check for potentially problematic characters
            var problematicChars = password.Where(c => char.IsControl(c) || c > 127).ToList();
            if (problematicChars.Any())
            {
                await _loggingService.LogWarningAsync(
                    $"Password contains {problematicChars.Count} potentially problematic characters (control chars or non-ASCII) - may affect typing accuracy");
            }

            return password;
        }

        /// <summary>
        /// Validates whether OTP is required for the specified account.
        /// </summary>
        /// <param name="account">PlayOnline member account to check</param>
        /// <returns>True if OTP is required, false otherwise</returns>
        /// <exception cref="ArgumentNullException">Thrown if account is null</exception>
        public Task<bool> IsOTPRequiredAsync(PlayOnlineMemberAccount account)
        {
            if (account == null)
                throw new ArgumentNullException(nameof(account));

            return Task.FromResult(account.IsOTPEnabled);
        }

        /// <summary>
        /// Generates a secure OTP code for the specified account with comprehensive validation.
        /// </summary>
        /// <param name="account">PlayOnline member account</param>
        /// <param name="profileFilePath">Profile file path for credential target generation</param>
        /// <returns>The generated OTP code, or null if generation fails</returns>
        /// <exception cref="ArgumentNullException">Thrown if account is null</exception>
        /// <remarks>
        /// Uses TOTP (Time-based One-Time Password) algorithm with the account's
        /// stored authentication key from Windows Credential Manager.
        /// Logs diagnostic information without exposing the full OTP code.
        /// </remarks>
        public async Task<string?> GenerateSecureOTPCodeAsync(
            PlayOnlineMemberAccount account,
            string profileFilePath)
        {
            if (account == null)
                throw new ArgumentNullException(nameof(account));

            if (!account.IsOTPEnabled)
            {
                await _loggingService.LogWarningAsync($"OTP generation requested but not enabled for account: {account.AccountName}");
                return null;
            }

            var otpCode = await _otpService.GenerateOTPCodeAsync(profileFilePath, account.Id);

            if (string.IsNullOrEmpty(otpCode))
            {
                await _loggingService.LogWarningAsync($"Could not generate OTP code for account: {account.AccountName}");
                return null;
            }

            // SECURITY: Log OTP generation without exposing the full code
            var maskedCode = otpCode.Length >= 2 ? $"{otpCode.Substring(0, 2)}****" : "****";
            await _loggingService.LogDebugAsync($"Generated OTP code for {account.AccountName}: {maskedCode} (Length: {otpCode.Length})");

            return otpCode;
        }

        /// <summary>
        /// Validates that OTP is properly configured for the specified account.
        /// Checks both OTP enablement and stored secret availability.
        /// </summary>
        /// <param name="account">PlayOnline member account to validate</param>
        /// <param name="profileFilePath">Profile file path for credential target generation</param>
        /// <returns>True if OTP is properly configured, false otherwise</returns>
        /// <exception cref="ArgumentNullException">Thrown if account is null</exception>
        public async Task<bool> ValidateOTPConfigurationAsync(
            PlayOnlineMemberAccount account,
            string profileFilePath)
        {
            if (account == null)
                throw new ArgumentNullException(nameof(account));

            if (!account.IsOTPEnabled)
            {
                await _loggingService.LogDebugAsync($"OTP not enabled for account: {account.AccountName}");
                return false;
            }

            // Verify the OTP secret exists
            var hasSecret = await _otpService.HasOTPSecretAsync(profileFilePath, account.Id);
            if (!hasSecret)
            {
                await _loggingService.LogWarningAsync($"OTP enabled but no secret stored for account: {account.AccountName}");
                return false;
            }

            await _loggingService.LogDebugAsync($"OTP configuration validated for account: {account.AccountName}");
            return true;
        }

        /// <summary>
        /// Validates that the account name is not empty.
        /// </summary>
        /// <param name="accountName">Account name to validate</param>
        /// <param name="propertyDescription">Description of the property for error messages</param>
        /// <returns>True if valid, false otherwise</returns>
        public async Task<bool> ValidateAccountPropertyAsync(string? accountName, string propertyDescription)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                await _loggingService.LogWarningAsync($"{propertyDescription} is missing or empty");
                return false;
            }

            return true;
        }
    }
}

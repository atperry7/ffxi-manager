namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for OTP (One-Time Password) operations using TOTP algorithm
    /// </summary>
    public interface IOTPService
    {
        /// <summary>
        /// Stores an OTP secret key securely in Windows Credential Manager
        /// </summary>
        /// <param name="profileFilePath">Profile file path for uniqueness</param>
        /// <param name="accountId">Account ID for uniqueness</param>
        /// <param name="authenticationKey">Square Enix authentication key (with or without spaces)</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> StoreOTPSecretAsync(string profileFilePath, Guid accountId, string authenticationKey);

        /// <summary>
        /// Generates the current 6-digit OTP code for the given account
        /// </summary>
        /// <param name="profileFilePath">Profile file path for uniqueness</param>
        /// <param name="accountId">Account ID for uniqueness</param>
        /// <returns>6-digit OTP code if successful, null if not found or error</returns>
        Task<string?> GenerateOTPCodeAsync(string profileFilePath, Guid accountId);

        /// <summary>
        /// Checks if an OTP secret is stored for the given account
        /// </summary>
        /// <param name="profileFilePath">Profile file path for uniqueness</param>
        /// <param name="accountId">Account ID for uniqueness</param>
        /// <returns>True if OTP secret exists, false otherwise</returns>
        Task<bool> HasOTPSecretAsync(string profileFilePath, Guid accountId);

        /// <summary>
        /// Deletes the stored OTP secret for the given account
        /// </summary>
        /// <param name="profileFilePath">Profile file path for uniqueness</param>
        /// <param name="accountId">Account ID for uniqueness</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> DeleteOTPSecretAsync(string profileFilePath, Guid accountId);

        /// <summary>
        /// Validates and normalizes a Square Enix authentication key
        /// </summary>
        /// <param name="authenticationKey">Raw authentication key from user input</param>
        /// <returns>Normalized key if valid, null if invalid</returns>
        string? ValidateAndNormalizeAuthenticationKey(string authenticationKey);

        /// <summary>
        /// Generates the credential target name for OTP secrets
        /// </summary>
        /// <param name="profileFilePath">Profile file path for uniqueness</param>
        /// <param name="accountId">Account ID for uniqueness</param>
        /// <returns>Standardized target name for OTP credentials</returns>
        string GenerateOTPCredentialTarget(string profileFilePath, Guid accountId);
    }
}
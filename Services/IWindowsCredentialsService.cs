using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for managing credentials using the Windows Credential Manager
    /// </summary>
    public interface IWindowsCredentialsService
    {
        /// <summary>
        /// Stores a password securely in Windows Credential Manager
        /// </summary>
        /// <param name="target">Unique identifier for the credential (e.g., "FFXIManager.Profile.AccountId")</param>
        /// <param name="username">Username associated with the credential</param>
        /// <param name="password">Password to store securely</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> StorePasswordAsync(string target, string username, string password);

        /// <summary>
        /// Retrieves a password from Windows Credential Manager
        /// </summary>
        /// <param name="target">Unique identifier for the credential</param>
        /// <param name="username">Username associated with the credential</param>
        /// <returns>The password if found, null if not found or on error</returns>
        Task<string?> RetrievePasswordAsync(string target, string username);

        /// <summary>
        /// Updates an existing password in Windows Credential Manager
        /// </summary>
        /// <param name="target">Unique identifier for the credential</param>
        /// <param name="username">Username associated with the credential</param>
        /// <param name="password">New password to store</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdatePasswordAsync(string target, string username, string password);

        /// <summary>
        /// Deletes a credential from Windows Credential Manager
        /// </summary>
        /// <param name="target">Unique identifier for the credential</param>
        /// <param name="username">Username associated with the credential</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> DeletePasswordAsync(string target, string username);

        /// <summary>
        /// Checks if a credential exists in Windows Credential Manager
        /// </summary>
        /// <param name="target">Unique identifier for the credential</param>
        /// <param name="username">Username associated with the credential</param>
        /// <returns>True if credential exists, false otherwise</returns>
        Task<bool> CredentialExistsAsync(string target, string username);

        /// <summary>
        /// Generates a standardized target name for FFXIManager credentials
        /// </summary>
        /// <param name="profileFilePath">Profile file path for uniqueness</param>
        /// <param name="accountId">Account ID for uniqueness</param>
        /// <returns>Standardized target name</returns>
        string GenerateCredentialTarget(string profileFilePath, Guid accountId);

        /// <summary>
        /// Finds credentials in Windows Credential Manager that are not linked to any current account
        /// </summary>
        /// <param name="profileFilePath">Profile file path to generate credential targets</param>
        /// <param name="knownAccountIds">List of account IDs that are currently in use</param>
        /// <returns>List of orphaned credentials found</returns>
        Task<List<OrphanedCredential>> FindOrphanedCredentialsAsync(string profileFilePath, List<Guid> knownAccountIds);
    }
}
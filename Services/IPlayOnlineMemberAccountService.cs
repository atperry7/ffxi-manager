using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for managing PlayOnline Member Account associations
    /// </summary>
    public interface IPlayOnlineMemberAccountService
    {
        /// <summary>
        /// Gets all accounts associated with a specific profile
        /// </summary>
        /// <param name="profileFilePath">The file path of the ProfileInfo (unique identifier)</param>
        /// <returns>List of associated accounts, empty list if none found</returns>
        Task<List<PlayOnlineMemberAccount>> GetAccountsForProfileAsync(string profileFilePath);

        /// <summary>
        /// Adds a new account association for a profile
        /// </summary>
        /// <param name="profileFilePath">The file path of the ProfileInfo</param>
        /// <param name="account">The account to add</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> AddAccountAsync(string profileFilePath, PlayOnlineMemberAccount account);

        /// <summary>
        /// Updates an existing account
        /// </summary>
        /// <param name="profileFilePath">The file path of the ProfileInfo</param>
        /// <param name="account">The account with updated information</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdateAccountAsync(string profileFilePath, PlayOnlineMemberAccount account);

        /// <summary>
        /// Deletes an account association
        /// </summary>
        /// <param name="profileFilePath">The file path of the ProfileInfo</param>
        /// <param name="accountId">The ID of the account to delete</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> DeleteAccountAsync(string profileFilePath, Guid accountId);

        /// <summary>
        /// Validates account settings
        /// </summary>
        /// <param name="account">The account to validate</param>
        /// <returns>Validation result with any error messages</returns>
        AccountValidationResult ValidateAccount(PlayOnlineMemberAccount account);

        /// <summary>
        /// Checks if a POL Member Slot is already in use for a profile
        /// </summary>
        /// <param name="profileFilePath">The file path of the ProfileInfo</param>
        /// <param name="polMemberSlot">The POL Member Slot to check</param>
        /// <param name="excludeAccountId">Optional account ID to exclude from check (for updates)</param>
        /// <returns>True if slot is in use, false otherwise</returns>
        Task<bool> IsSlotInUseAsync(string profileFilePath, int polMemberSlot, Guid? excludeAccountId = null);

        /// <summary>
        /// Gets all profiles that have account associations
        /// </summary>
        /// <returns>Dictionary of profile paths and their account counts</returns>
        Task<Dictionary<string, int>> GetProfileAccountCountsAsync();

        /// <summary>
        /// Removes all account associations for a profile
        /// </summary>
        /// <param name="profileFilePath">The file path of the ProfileInfo</param>
        /// <returns>Number of accounts removed</returns>
        Task<int> RemoveAllAccountsForProfileAsync(string profileFilePath);
    }

    /// <summary>
    /// Result of account validation
    /// </summary>
    public class AccountValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();

        public static AccountValidationResult Success() => new AccountValidationResult { IsValid = true };
        public static AccountValidationResult Failure(params string[] errors) => new AccountValidationResult
        {
            IsValid = false,
            Errors = new List<string>(errors)
        };
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Models.Settings;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for managing PlayOnline Member Account associations
    /// </summary>
    public class PlayOnlineMemberAccountService : IPlayOnlineMemberAccountService
    {
        private readonly ISettingsService _settingsService;
        private readonly ILoggingService _loggingService;
        private readonly IWindowsCredentialsService _credentialsService;
        private readonly object _lockObject = new();

        public PlayOnlineMemberAccountService(
            ISettingsService settingsService,
            ILoggingService loggingService,
            IWindowsCredentialsService credentialsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
        }

        public Task<List<PlayOnlineMemberAccount>> GetAccountsForProfileAsync(string profileFilePath)
        {
            return Task.Run(async () =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        var settings = _settingsService.LoadSettings();
                        if (settings.PlayOnlineMemberAccounts.TryGetValue(profileFilePath, out var accounts))
                        {
                            var accountList = accounts ?? new List<PlayOnlineMemberAccount>();

                            // Update HasStoredPassword status for each account
                            foreach (var account in accountList)
                            {
                                var target = _credentialsService.GenerateCredentialTarget(profileFilePath, account.Id);
                                account.HasStoredPassword = _credentialsService.CredentialExistsAsync(target, account.AccountName).Result;
                            }

                            return accountList;
                        }
                        return new List<PlayOnlineMemberAccount>();
                    }
                    catch (Exception ex)
                    {
                        _ = _loggingService.LogErrorAsync("Error getting accounts for profile", ex);
                        return new List<PlayOnlineMemberAccount>();
                    }
                }
            });
        }

        public Task<bool> AddAccountAsync(string profileFilePath, PlayOnlineMemberAccount account)
        {
            return Task.Run(async () =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        // Validate the account
                        var validation = ValidateAccount(account);
                        if (!validation.IsValid)
                        {
                            _ = _loggingService.LogWarningAsync($"Account validation failed: {string.Join(", ", validation.Errors)}");
                            return false;
                        }

                        var settings = _settingsService.LoadSettings();

                        // Initialize the dictionary if needed
                        if (!settings.PlayOnlineMemberAccounts.TryGetValue(profileFilePath, out var accountList))
                        {
                            accountList = new List<PlayOnlineMemberAccount>();
                            settings.PlayOnlineMemberAccounts[profileFilePath] = accountList;
                        }

                        // Check for duplicate POL slot
                        var existingSlot = settings.PlayOnlineMemberAccounts[profileFilePath]
                            .FirstOrDefault(a => a.POLMemberSlot == account.POLMemberSlot);
                        if (existingSlot != null)
                        {
                            _ = _loggingService.LogWarningAsync($"POL Member Slot {account.POLMemberSlot} is already in use");
                            return false;
                        }

                        // Add the account
                        settings.PlayOnlineMemberAccounts[profileFilePath].Add(account);

                        // Save settings
                        _settingsService.SaveSettings(settings);

                        _ = _loggingService.LogInfoAsync($"Added account {account.DisplayName} to profile {profileFilePath}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _ = _loggingService.LogErrorAsync("Error adding account", ex);
                        return false;
                    }
                }
            });
        }

        public Task<bool> UpdateAccountAsync(string profileFilePath, PlayOnlineMemberAccount account)
        {
            return Task.Run(async () =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        // Validate the account
                        var validation = ValidateAccount(account);
                        if (!validation.IsValid)
                        {
                            _ = _loggingService.LogWarningAsync($"Account validation failed: {string.Join(", ", validation.Errors)}");
                            return false;
                        }

                        var settings = _settingsService.LoadSettings();

                        if (!settings.PlayOnlineMemberAccounts.TryGetValue(profileFilePath, out var accounts))
                        {
                            _ = _loggingService.LogWarningAsync($"No accounts found for profile {profileFilePath}");
                            return false;
                        }

                        var existingIndex = accounts.FindIndex(a => a.Id == account.Id);
                        if (existingIndex == -1)
                        {
                            _ = _loggingService.LogWarningAsync($"Account {account.Id} not found");
                            return false;
                        }

                        // Check for duplicate POL slot (excluding current account)
                        var duplicateSlot = accounts.FirstOrDefault(a =>
                            a.Id != account.Id && a.POLMemberSlot == account.POLMemberSlot);
                        if (duplicateSlot != null)
                        {
                            _ = _loggingService.LogWarningAsync($"POL Member Slot {account.POLMemberSlot} is already in use");
                            return false;
                        }

                        // Update the account
                        accounts[existingIndex] = account;

                        // Save settings
                        _settingsService.SaveSettings(settings);

                        _ = _loggingService.LogInfoAsync($"Updated account {account.DisplayName} for profile {profileFilePath}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _ = _loggingService.LogErrorAsync("Error updating account", ex);
                        return false;
                    }
                }
            });
        }

        public Task<bool> DeleteAccountAsync(string profileFilePath, Guid accountId)
        {
            return Task.Run(() =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        var settings = _settingsService.LoadSettings();

                        if (!settings.PlayOnlineMemberAccounts.TryGetValue(profileFilePath, out var accounts))
                        {
                            _ = _loggingService.LogWarningAsync($"No accounts found for profile {profileFilePath}");
                            return false;
                        }

                        var accountToRemove = accounts.FirstOrDefault(a => a.Id == accountId);
                        if (accountToRemove == null)
                        {
                            _ = _loggingService.LogWarningAsync($"Account {accountId} not found");
                            return false;
                        }

                        // Remove stored password from Windows Credentials
                        var target = _credentialsService.GenerateCredentialTarget(profileFilePath, accountId);
                        var username = string.IsNullOrWhiteSpace(accountToRemove.AccountName)
                            ? $"Slot{accountToRemove.POLMemberSlot}-{accountToRemove.FFXICharacterSlot}"
                            : accountToRemove.AccountName;
                        _ = _credentialsService.DeletePasswordAsync(target, username);

                        // Remove the account
                        accounts.Remove(accountToRemove);

                        // Clean up empty entries
                        if (accounts.Count == 0)
                        {
                            settings.PlayOnlineMemberAccounts.Remove(profileFilePath);
                        }

                        // Save settings
                        _settingsService.SaveSettings(settings);

                        _ = _loggingService.LogInfoAsync($"Deleted account {accountToRemove.DisplayName} from profile {profileFilePath}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _ = _loggingService.LogErrorAsync("Error deleting account", ex);
                        return false;
                    }
                }
            });
        }

        public AccountValidationResult ValidateAccount(PlayOnlineMemberAccount account)
        {
            var errors = new List<string>();

            if (account == null)
            {
                return AccountValidationResult.Failure("Account cannot be null");
            }

            // Validate POL Member Slot
            if (account.POLMemberSlot < 1 || account.POLMemberSlot > 4)
            {
                errors.Add("POL Member Slot must be between 1 and 4");
            }

            // Validate FFXI Character Slot
            if (account.FFXICharacterSlot < 1 || account.FFXICharacterSlot > 16)
            {
                errors.Add("FFXI Character Slot must be between 1 and 16");
            }

            // Note: Password validation is handled separately through Windows Credentials service
            // The HasStoredPassword property indicates if a password is securely stored

            return errors.Count == 0 ? AccountValidationResult.Success() : AccountValidationResult.Failure(errors.ToArray());
        }

        public Task<bool> IsSlotInUseAsync(string profileFilePath, int polMemberSlot, Guid? excludeAccountId = null)
        {
            return Task.Run(() =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        var settings = _settingsService.LoadSettings();

                        if (!settings.PlayOnlineMemberAccounts.TryGetValue(profileFilePath, out var accounts))
                        {
                            return false;
                        }

                        return accounts.Any(a =>
                            a.POLMemberSlot == polMemberSlot &&
                            (!excludeAccountId.HasValue || a.Id != excludeAccountId.Value));
                    }
                    catch (Exception ex)
                    {
                        _ = _loggingService.LogErrorAsync("Error checking slot usage", ex);
                        return false;
                    }
                }
            });
        }

        public Task<Dictionary<string, int>> GetProfileAccountCountsAsync()
        {
            return Task.Run(() =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        var settings = _settingsService.LoadSettings();
                        return settings.PlayOnlineMemberAccounts
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.Count ?? 0);
                    }
                    catch (Exception ex)
                    {
                        _ = _loggingService.LogErrorAsync("Error getting profile account counts", ex);
                        return new Dictionary<string, int>();
                    }
                }
            });
        }

        public Task<int> RemoveAllAccountsForProfileAsync(string profileFilePath)
        {
            return Task.Run(() =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        var settings = _settingsService.LoadSettings();

                        if (!settings.PlayOnlineMemberAccounts.TryGetValue(profileFilePath, out var accounts))
                        {
                            return 0;
                        }

                        var count = accounts?.Count ?? 0;
                        settings.PlayOnlineMemberAccounts.Remove(profileFilePath);

                        // Save settings
                        _settingsService.SaveSettings(settings);

                        _ = _loggingService.LogInfoAsync($"Removed {count} accounts from profile {profileFilePath}");
                        return count;
                    }
                    catch (Exception ex)
                    {
                        _ = _loggingService.LogErrorAsync("Error removing all accounts for profile", ex);
                        return 0;
                    }
                }
            });
        }

        public Task<bool> SetAccountPasswordAsync(string profileFilePath, Guid accountId, string password)
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(profileFilePath))
                        throw new ArgumentException("Profile file path cannot be null or empty", nameof(profileFilePath));
                    if (string.IsNullOrWhiteSpace(password))
                        throw new ArgumentException("Password cannot be null or empty", nameof(password));

                    // Get the account to find the username
                    var accounts = await GetAccountsForProfileAsync(profileFilePath);
                    var account = accounts.FirstOrDefault(a => a.Id == accountId);
                    if (account == null)
                    {
                        await _loggingService.LogWarningAsync($"Account {accountId} not found for password storage");
                        return false;
                    }

                    var target = _credentialsService.GenerateCredentialTarget(profileFilePath, accountId);
                    var username = string.IsNullOrWhiteSpace(account.AccountName) ? $"Slot{account.POLMemberSlot}-{account.FFXICharacterSlot}" : account.AccountName;

                    bool result = await _credentialsService.StorePasswordAsync(target, username, password);
                    if (result)
                    {
                        // Update the HasStoredPassword property
                        account.HasStoredPassword = true;
                        await _loggingService.LogInfoAsync($"Password stored securely for account {account.DisplayName}");
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync("Error setting account password", ex);
                    return false;
                }
            });
        }

        public Task<string?> GetAccountPasswordAsync(string profileFilePath, Guid accountId)
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(profileFilePath))
                        throw new ArgumentException("Profile file path cannot be null or empty", nameof(profileFilePath));

                    // Get the account to find the username
                    var accounts = await GetAccountsForProfileAsync(profileFilePath);
                    var account = accounts.FirstOrDefault(a => a.Id == accountId);
                    if (account == null)
                    {
                        await _loggingService.LogWarningAsync($"Account {accountId} not found for password retrieval");
                        return null;
                    }

                    var target = _credentialsService.GenerateCredentialTarget(profileFilePath, accountId);
                    var username = string.IsNullOrWhiteSpace(account.AccountName) ? $"Slot{account.POLMemberSlot}-{account.FFXICharacterSlot}" : account.AccountName;

                    var password = await _credentialsService.RetrievePasswordAsync(target, username);
                    if (password != null)
                    {
                        await _loggingService.LogDebugAsync($"Password retrieved successfully for account {account.DisplayName}");
                    }

                    return password;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync("Error getting account password", ex);
                    return null;
                }
            });
        }

        public Task<bool> RemoveAccountPasswordAsync(string profileFilePath, Guid accountId)
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(profileFilePath))
                        throw new ArgumentException("Profile file path cannot be null or empty", nameof(profileFilePath));

                    // Get the account to find the username
                    var accounts = await GetAccountsForProfileAsync(profileFilePath);
                    var account = accounts.FirstOrDefault(a => a.Id == accountId);
                    if (account == null)
                    {
                        await _loggingService.LogWarningAsync($"Account {accountId} not found for password removal");
                        return false;
                    }

                    var target = _credentialsService.GenerateCredentialTarget(profileFilePath, accountId);
                    var username = string.IsNullOrWhiteSpace(account.AccountName) ? $"Slot{account.POLMemberSlot}-{account.FFXICharacterSlot}" : account.AccountName;

                    bool result = await _credentialsService.DeletePasswordAsync(target, username);
                    if (result)
                    {
                        // Update the HasStoredPassword property
                        account.HasStoredPassword = false;
                        await _loggingService.LogInfoAsync($"Password removed for account {account.DisplayName}");
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync("Error removing account password", ex);
                    return false;
                }
            });
        }
    }
}
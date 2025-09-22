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
        private readonly object _lockObject = new();

        public PlayOnlineMemberAccountService(
            ISettingsService settingsService,
            ILoggingService loggingService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public Task<List<PlayOnlineMemberAccount>> GetAccountsForProfileAsync(string profileFilePath)
        {
            return Task.Run(() =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        var settings = _settingsService.LoadSettings();
                        if (settings.PlayOnlineMemberAccounts.TryGetValue(profileFilePath, out var accounts))
                        {
                            return accounts ?? new List<PlayOnlineMemberAccount>();
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

            // TODO: Add password validation when implementing secure storage
            // For MVP, we allow empty passwords but log a warning
            if (string.IsNullOrWhiteSpace(account.POLPassword))
            {
                _ = _loggingService.LogDebugAsync("Account has no password set - this may cause auto-login issues");
            }

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
    }
}
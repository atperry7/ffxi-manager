using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin;
using FFXIManager.ViewModels.Base;
using FFXIManager.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// ViewModel for managing PlayOnline Member Account associations
    /// </summary>
    public class PlayOnlineMemberAccountsViewModel : ViewModelBase, IDisposable
    {
        private readonly IPlayOnlineMemberAccountService _accountService;
        private readonly IStatusMessageService _statusService;
        private readonly ILoggingService _loggingService;
        private readonly IDialogService _dialogService;
        private readonly IUiDispatcher _uiDispatcher;
        private readonly IOTPService _otpService;
        private readonly IAutoLoginQueueService _queueService;
        private readonly IWindowsCredentialsService _credentialsService;
        private readonly IWorkflowService _workflowService;

        private ProfileInfo? _currentProfile;
        private PlayOnlineMemberAccount? _selectedAccount;
        private bool _isLoading;
        private string _accountsHeader = "PlayOnline Member Accounts";
        private DispatcherTimer? _otpRefreshTimer;
        private bool _disposed;

        public PlayOnlineMemberAccountsViewModel(
            IPlayOnlineMemberAccountService accountService,
            IStatusMessageService statusService,
            ILoggingService loggingService,
            IDialogService dialogService,
            IUiDispatcher uiDispatcher,
            IOTPService otpService,
            IAutoLoginQueueService queueService,
            IWindowsCredentialsService credentialsService,
            IWorkflowService workflowService)
        {
            _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
            _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));

            Accounts = new ObservableCollection<PlayOnlineMemberAccount>();
            Accounts.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(CanAddAccount));
                UpdateCommandStates();
            };
            InitializeCommands();
            InitializeOTPTimer();
        }

        #region Properties

        /// <summary>
        /// Collection of accounts for the current profile
        /// </summary>
        public ObservableCollection<PlayOnlineMemberAccount> Accounts { get; }

        /// <summary>
        /// Currently selected profile
        /// </summary>
        public ProfileInfo? CurrentProfile
        {
            get => _currentProfile;
            set
            {
                if (SetProperty(ref _currentProfile, value))
                {
                    // Stop timer when profile changes since all OTP codes will be cleared
                    _otpRefreshTimer?.Stop();

                    _ = RefreshAccountsAsync();
                    UpdateAccountsHeader();
                    OnPropertyChanged(nameof(HasProfileSelected));
                    OnPropertyChanged(nameof(CanAddAccount));
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Currently selected account
        /// </summary>
        public PlayOnlineMemberAccount? SelectedAccount
        {
            get => _selectedAccount;
            set
            {
                if (SetProperty(ref _selectedAccount, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Loading state
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Header text for the accounts section
        /// </summary>
        public string AccountsHeader
        {
            get => _accountsHeader;
            set => SetProperty(ref _accountsHeader, value);
        }

        /// <summary>
        /// Indicates if a profile is selected
        /// </summary>
        public bool HasProfileSelected => CurrentProfile != null && !CurrentProfile.IsSystemFile;

        /// <summary>
        /// Indicates if accounts can be added (max 4 per POL limitation)
        /// </summary>
        public bool CanAddAccount => HasProfileSelected && Accounts.Count < 4;

        #endregion

        #region Commands

        public ICommand AddAccountCommand { get; private set; } = null!;
        public ICommand EditAccountCommand { get; private set; } = null!;
        public ICommand DeleteAccountCommand { get; private set; } = null!;
        public ICommand RefreshCommand { get; private set; } = null!;
        public ICommand LoginNowCommand { get; private set; } = null!;
        public ICommand AddToQueueCommand { get; private set; } = null!;
        public ICommand RecoverCredentialsCommand { get; private set; } = null!;

        // Parameter-based commands for context menu
        public ICommand EditAccountParameterCommand { get; private set; } = null!;
        public ICommand DeleteAccountParameterCommand { get; private set; } = null!;
        public ICommand ToggleOTPVisibilityCommand { get; private set; } = null!;
        public ICommand AddToQueueParameterCommand { get; private set; } = null!;
        public ICommand LoginNowParameterCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            AddAccountCommand = new RelayCommand(async () => await AddAccountAsync(), () => CanAddAccount);
            EditAccountCommand = new RelayCommand(async () => await EditSelectedAccountAsync(), () => SelectedAccount != null);
            DeleteAccountCommand = new RelayCommand(async () => await DeleteSelectedAccountAsync(), () => SelectedAccount != null);
            RefreshCommand = new RelayCommand(async () => await RefreshAccountsAsync());
            LoginNowCommand = new RelayCommand(async () => await LoginNowSelectedAsync(), () => SelectedAccount != null);
            AddToQueueCommand = new RelayCommand(async () => await AddToQueueSelectedAsync(), () => SelectedAccount != null);
            RecoverCredentialsCommand = new RelayCommand(async () => await RecoverCredentialsAsync(), () => HasProfileSelected);

            EditAccountParameterCommand = new RelayCommandWithParameter<PlayOnlineMemberAccount>(async account => await EditAccountAsync(account));
            DeleteAccountParameterCommand = new RelayCommandWithParameter<PlayOnlineMemberAccount>(async account => await DeleteAccountAsync(account));
            ToggleOTPVisibilityCommand = new RelayCommandWithParameter<PlayOnlineMemberAccount>(async account => await ToggleOTPVisibilityAsync(account));
            AddToQueueParameterCommand = new RelayCommandWithParameter<PlayOnlineMemberAccount>(async account => await AddToQueueAsync(account));
            LoginNowParameterCommand = new RelayCommandWithParameter<PlayOnlineMemberAccount>(async account => await LoginNowAsync(account));
        }

        #endregion

        #region Command Implementations

        private async Task AddAccountAsync()
        {
            if (CurrentProfile == null || CurrentProfile.IsSystemFile)
            {
                _statusService.SetTemporaryMessage("Please select a non-system profile first", TimeSpan.FromSeconds(3));
                return;
            }

            if (Accounts.Count >= 4)
            {
                _statusService.SetTemporaryMessage("Maximum of 4 PlayOnline Member Accounts allowed", TimeSpan.FromSeconds(3));
                return;
            }

            var newAccount = new PlayOnlineMemberAccount
            {
                POLMemberSlot = GetNextAvailableSlot(),
                FFXICharacterSlot = 1
            };

            var dialog = new PlayOnlineMemberAccountEditDialog
            {
                DataContext = new PlayOnlineMemberAccountEditViewModel(newAccount, Accounts.ToList(), _workflowService)
            };

            if (dialog.ShowDialog() == true)
            {
                var editVm = (PlayOnlineMemberAccountEditViewModel)dialog.DataContext;
                var success = await _accountService.AddAccountAsync(CurrentProfile.FilePath, editVm.Account);

                if (success)
                {
                    // Store password if provided
                    if (!string.IsNullOrWhiteSpace(dialog.EnteredPassword))
                    {
                        await _accountService.SetAccountPasswordAsync(CurrentProfile.FilePath, editVm.Account.Id, dialog.EnteredPassword);
                        editVm.Account.HasStoredPassword = true;
                    }

                    // Store OTP authentication key if provided
                    if (editVm.Account.OTPConfiguration?.IsEnabled == true && !string.IsNullOrWhiteSpace(dialog.EnteredAuthenticationKey))
                    {
                        var otpStored = await _otpService.StoreOTPSecretAsync(CurrentProfile.FilePath, editVm.Account.Id, dialog.EnteredAuthenticationKey);
                        if (otpStored)
                        {
                            editVm.Account.OTPConfiguration.HasStoredSecret = true;
                        }
                        else
                        {
                            _statusService.SetTemporaryMessage("Failed to store OTP authentication key", TimeSpan.FromSeconds(3));
                        }
                    }

                    await RefreshAccountsAsync();
                    _statusService.SetTemporaryMessage($"Added account: {editVm.Account.DisplayName}", TimeSpan.FromSeconds(3));
                }
                else
                {
                    _statusService.SetTemporaryMessage("Failed to add account", TimeSpan.FromSeconds(3));
                }
            }
        }

        private async Task EditSelectedAccountAsync()
        {
            if (SelectedAccount != null)
            {
                await EditAccountAsync(SelectedAccount);
            }
        }

        private async Task EditAccountAsync(PlayOnlineMemberAccount account)
        {
            if (CurrentProfile == null || account == null)
                return;

            // Create a copy for editing
            var editAccount = new PlayOnlineMemberAccount
            {
                Id = account.Id,
                POLMemberSlot = account.POLMemberSlot,
                FFXICharacterSlot = account.FFXICharacterSlot,
                AccountName = account.AccountName,
                WorkflowId = account.WorkflowId,
                HasStoredPassword = account.HasStoredPassword,
                OTPConfiguration = account.OTPConfiguration != null
                    ? new OTPConfiguration
                    {
                        IsEnabled = account.OTPConfiguration.IsEnabled,
                        HasStoredSecret = account.OTPConfiguration.HasStoredSecret,
                        ProviderName = account.OTPConfiguration.ProviderName
                    }
                    : null
            };

            var dialog = new PlayOnlineMemberAccountEditDialog
            {
                DataContext = new PlayOnlineMemberAccountEditViewModel(
                    editAccount,
                    Accounts.Where(a => a.Id != account.Id).ToList(),
                    _workflowService)
            };

            if (dialog.ShowDialog() == true)
            {
                var editVm = (PlayOnlineMemberAccountEditViewModel)dialog.DataContext;
                var success = await _accountService.UpdateAccountAsync(CurrentProfile.FilePath, editVm.Account);

                if (success)
                {
                    // Update password if provided
                    if (!string.IsNullOrWhiteSpace(dialog.EnteredPassword))
                    {
                        await _accountService.SetAccountPasswordAsync(CurrentProfile.FilePath, editVm.Account.Id, dialog.EnteredPassword);
                        editVm.Account.HasStoredPassword = true;
                    }

                    // Update OTP authentication key if provided
                    if (editVm.Account.OTPConfiguration?.IsEnabled == true && !string.IsNullOrWhiteSpace(dialog.EnteredAuthenticationKey))
                    {
                        var otpStored = await _otpService.StoreOTPSecretAsync(CurrentProfile.FilePath, editVm.Account.Id, dialog.EnteredAuthenticationKey);
                        if (otpStored)
                        {
                            editVm.Account.OTPConfiguration.HasStoredSecret = true;
                        }
                        else
                        {
                            _statusService.SetTemporaryMessage("Failed to store OTP authentication key", TimeSpan.FromSeconds(3));
                        }
                    }
                    else if (editVm.Account.OTPConfiguration?.IsEnabled == false)
                    {
                        // OTP was disabled, remove stored secret
                        await _otpService.DeleteOTPSecretAsync(CurrentProfile.FilePath, editVm.Account.Id);
                        editVm.Account.OTPConfiguration.HasStoredSecret = false;
                    }

                    await RefreshAccountsAsync();
                    _statusService.SetTemporaryMessage($"Updated account: {editVm.Account.DisplayName}", TimeSpan.FromSeconds(3));
                }
                else
                {
                    _statusService.SetTemporaryMessage("Failed to update account", TimeSpan.FromSeconds(3));
                }
            }
        }

        private async Task DeleteSelectedAccountAsync()
        {
            if (SelectedAccount != null)
            {
                await DeleteAccountAsync(SelectedAccount);
            }
        }

        private async Task DeleteAccountAsync(PlayOnlineMemberAccount account)
        {
            if (CurrentProfile == null || account == null)
                return;

            var result = await _dialogService.ShowConfirmationDialogAsync(
                "Delete Account",
                $"Are you sure you want to delete the account '{account.DisplayName}'?");

            if (result)
            {
                var success = await _accountService.DeleteAccountAsync(CurrentProfile.FilePath, account.Id);

                if (success)
                {
                    // Clean up OTP secret if it exists
                    if (account.OTPConfiguration?.HasStoredSecret == true)
                    {
                        await _otpService.DeleteOTPSecretAsync(CurrentProfile.FilePath, account.Id);
                    }

                    await RefreshAccountsAsync();
                    _statusService.SetTemporaryMessage($"Deleted account: {account.DisplayName}", TimeSpan.FromSeconds(3));
                }
                else
                {
                    _statusService.SetTemporaryMessage("Failed to delete account", TimeSpan.FromSeconds(3));
                }
            }
        }

        private async Task ToggleOTPVisibilityAsync(PlayOnlineMemberAccount account)
        {
            if (CurrentProfile == null || account == null || !account.IsOTPEnabled)
                return;

            try
            {
                if (account.IsOTPCodeVisible)
                {
                    // Hide the code
                    account.IsOTPCodeVisible = false;
                    account.CurrentOTPCode = null;
                    account.OTPTimeRemaining = 100.0; // Reset progress
                }
                else
                {
                    // Show the code - generate current OTP
                    var otpCode = await _otpService.GenerateOTPCodeAsync(CurrentProfile.FilePath, account.Id);
                    if (otpCode != null)
                    {
                        account.CurrentOTPCode = otpCode;
                        account.IsOTPCodeVisible = true;

                        // Initialize progress for the current time window
                        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var secondsInWindow = (int)(unixTime % 30);
                        account.OTPTimeRemaining = (30 - secondsInWindow) / 30.0 * 100.0;

                        // Start timer if this is the first visible OTP
                        CheckAndStartOTPTimer();
                    }
                    else
                    {
                        _statusService.SetTemporaryMessage("Failed to generate OTP code", TimeSpan.FromSeconds(3));
                    }
                }

                // Check if we should stop the timer (no visible OTPs)
                CheckAndStopOTPTimer();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error toggling OTP visibility", ex);
                _statusService.SetTemporaryMessage("Error displaying OTP code", TimeSpan.FromSeconds(3));
            }
        }

        private async Task RefreshAccountsAsync()
        {
            if (CurrentProfile == null || CurrentProfile.IsSystemFile)
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    // Stop timer before clearing accounts to prevent accessing deleted accounts
                    _otpRefreshTimer?.Stop();
                    Accounts.Clear();
                    OnPropertyChanged(nameof(HasProfileSelected));
                    OnPropertyChanged(nameof(CanAddAccount));
                });
                return;
            }

            IsLoading = true;
            try
            {
                var accounts = await _accountService.GetAccountsForProfileAsync(CurrentProfile.FilePath);

                await _uiDispatcher.InvokeAsync(() =>
                {
                    // Stop timer before clearing accounts to prevent accessing deleted accounts
                    _otpRefreshTimer?.Stop();
                    Accounts.Clear();
                    foreach (var account in accounts.OrderBy(a => a.POLMemberSlot))
                    {
                        Accounts.Add(account);
                    }

                    OnPropertyChanged(nameof(HasProfileSelected));
                    OnPropertyChanged(nameof(CanAddAccount));
                });

                await _loggingService.LogDebugAsync($"Loaded {accounts.Count} accounts for profile {CurrentProfile.Name}");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error loading accounts", ex);
                _statusService.SetTemporaryMessage("Failed to load accounts", TimeSpan.FromSeconds(3));
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RecoverCredentialsAsync()
        {
            if (CurrentProfile == null || CurrentProfile.IsSystemFile)
            {
                _statusService.SetTemporaryMessage("Please select a non-system profile first", TimeSpan.FromSeconds(3));
                return;
            }

            try
            {
                // Find orphaned credentials
                var knownAccountIds = Accounts.Select(a => a.Id).ToList();
                var orphanedCredentials = await _credentialsService.FindOrphanedCredentialsAsync(CurrentProfile.FilePath, knownAccountIds);

                if (orphanedCredentials.Count == 0)
                {
                    _statusService.SetTemporaryMessage("No orphaned credentials found", TimeSpan.FromSeconds(3));
                    return;
                }

                // Show recovery dialog
                var dialog = new CredentialRecoveryDialog();
                dialog.SetOrphanedCredentials(new ObservableCollection<OrphanedCredential>(orphanedCredentials));

                if (dialog.ShowDialog() == true && dialog.RestoredCredential != null)
                {
                    // Create a new account with the orphaned credential's ID
                    var restoredCredential = dialog.RestoredCredential;

                    // Use the username from the password credential if available, otherwise use a default
                    var accountName = !string.IsNullOrWhiteSpace(restoredCredential.PasswordUsername)
                        ? restoredCredential.PasswordUsername
                        : $"Recovered {restoredCredential.ShortId}";

                    var newAccount = new PlayOnlineMemberAccount
                    {
                        Id = restoredCredential.AccountId,
                        POLMemberSlot = GetNextAvailableSlot(),
                        FFXICharacterSlot = 1,
                        AccountName = accountName,
                        HasStoredPassword = restoredCredential.HasPassword,
                        OTPConfiguration = restoredCredential.HasOtp
                            ? new OTPConfiguration
                            {
                                IsEnabled = true,
                                HasStoredSecret = true,
                                ProviderName = "Square Enix"
                            }
                            : null
                    };

                    var success = await _accountService.AddAccountAsync(CurrentProfile.FilePath, newAccount);
                    if (success)
                    {
                        await RefreshAccountsAsync();
                        _statusService.SetTemporaryMessage($"Restored account with credentials from {restoredCredential.ShortId}", TimeSpan.FromSeconds(3));
                    }
                    else
                    {
                        _statusService.SetTemporaryMessage("Failed to restore account", TimeSpan.FromSeconds(3));
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error recovering credentials", ex);
                _statusService.SetTemporaryMessage("Error recovering credentials", TimeSpan.FromSeconds(3));
            }
        }

        #endregion

        #region OTP Timer Management

        private void InitializeOTPTimer()
        {
            _otpRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1) // Update every second for smooth progress bars
            };
            _otpRefreshTimer.Tick += async (_, _) => await UpdateOTPCodesAndProgressAsync();
        }

        private void CheckAndStartOTPTimer()
        {
            if (_otpRefreshTimer?.IsEnabled != true && HasVisibleOTPCodes())
            {
                _otpRefreshTimer?.Start();
                _loggingService.LogDebugAsync("Started OTP refresh timer");
            }
        }

        private void CheckAndStopOTPTimer()
        {
            if (_otpRefreshTimer?.IsEnabled == true && !HasVisibleOTPCodes())
            {
                _otpRefreshTimer?.Stop();
                _loggingService.LogDebugAsync("Stopped OTP refresh timer");
            }
        }

        private bool HasVisibleOTPCodes()
        {
            try
            {
                return Accounts?.Any(a => a != null && a.IsOTPCodeVisible) == true;
            }
            catch
            {
                // Collection modified during enumeration - assume no visible codes
                return false;
            }
        }

        private async Task UpdateOTPCodesAndProgressAsync()
        {
            // Safety check - if disposed or invalid state, stop timer
            if (_disposed || CurrentProfile == null || Accounts == null)
            {
                _otpRefreshTimer?.Stop();
                return;
            }

            // Get visible accounts safely
            List<PlayOnlineMemberAccount> visibleAccounts;
            try
            {
                visibleAccounts = Accounts.Where(a => a != null && a.IsOTPCodeVisible).ToList();
            }
            catch (Exception ex)
            {
                // Collection may have been modified during enumeration
                await _loggingService.LogWarningAsync("OTP timer: Accounts collection modified during enumeration", ex);
                _otpRefreshTimer?.Stop();
                return;
            }

            if (visibleAccounts.Count == 0)
            {
                CheckAndStopOTPTimer();
                return;
            }

            // Calculate current time within TOTP 30-second window
            var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var secondsInWindow = (int)(unixTime % 30);
            var progressPercentage = (30 - secondsInWindow) / 30.0 * 100.0;

            // Update progress for all visible accounts
            foreach (var account in visibleAccounts)
            {
                account.OTPTimeRemaining = progressPercentage;
            }

            // Refresh OTP codes when we hit a new 30-second window (secondsInWindow == 0 or close to it)
            if (secondsInWindow <= 1) // Refresh in the first second of each new window
            {
                foreach (var account in visibleAccounts)
                {
                    try
                    {
                        var otpCode = await _otpService.GenerateOTPCodeAsync(CurrentProfile.FilePath, account.Id);
                        if (otpCode != null)
                        {
                            account.CurrentOTPCode = otpCode;
                        }
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogErrorAsync($"Error refreshing OTP code for account {account.AccountName}", ex);
                        // Don't stop the timer for individual account failures
                    }
                }

                await _loggingService.LogDebugAsync($"Refreshed {visibleAccounts.Count} visible OTP codes");
            }
        }

        /// <summary>
        /// Adds the specified account to the auto-login queue
        /// </summary>
        /// <param name="account">The account to add to the queue</param>
        private async Task AddToQueueAsync(PlayOnlineMemberAccount account)
        {
            if (account == null || CurrentProfile == null)
            {
                _statusService.SetTemporaryMessage("Invalid account or profile selected", TimeSpan.FromSeconds(3));
                return;
            }

            // Validate that the account has a stored password
            if (!account.HasStoredPassword)
            {
                _statusService.SetTemporaryMessage($"Cannot add {account.DisplayName} to queue - no password stored. Please edit the account and set a password first.", TimeSpan.FromSeconds(5));
                await _loggingService.LogWarningAsync($"Attempted to add account {account.DisplayName} to queue without stored password");
                return;
            }

            try
            {
                await _queueService.AddToQueueAsync(account, CurrentProfile);
                _statusService.SetTemporaryMessage($"Added {account.DisplayName} to auto-login queue", TimeSpan.FromSeconds(3));
                await _loggingService.LogInfoAsync($"Added account {account.DisplayName} from profile {CurrentProfile.Name} to auto-login queue");
            }
            catch (Exception ex)
            {
                _statusService.SetTemporaryMessage($"Failed to add {account.DisplayName} to queue", TimeSpan.FromSeconds(3));
                await _loggingService.LogErrorAsync($"Error adding account to queue: {account.DisplayName}", ex);
            }
        }

        private async Task LoginNowAsync(PlayOnlineMemberAccount account)
        {
            if (account == null || CurrentProfile == null)
            {
                _statusService.SetTemporaryMessage("Invalid account or profile selected", TimeSpan.FromSeconds(3));
                return;
            }

            // Validate that the account has a stored password
            if (!account.HasStoredPassword)
            {
                _statusService.SetTemporaryMessage($"Cannot login {account.DisplayName} - no password stored. Please edit the account and set a password first.", TimeSpan.FromSeconds(5));
                await _loggingService.LogWarningAsync($"Attempted to login account {account.DisplayName} without stored password");
                return;
            }

            if (_queueService.IsExecuting)
            {
                _statusService.SetTemporaryMessage("Cannot start login now - queue is already executing", TimeSpan.FromSeconds(3));
                return;
            }

            try
            {
                // Store original queue item statuses for potential restoration
                var originalStatuses = new Dictionary<Guid, AutoLoginQueueStatus>();
                var queueItems = _queueService.QueueItems.ToList();

                // Check if the account is already in the queue
                var existingItem = queueItems.FirstOrDefault(item => item.Account.Id == account.Id);

                if (existingItem != null)
                {
                    // Account is already in queue
                    await _loggingService.LogInfoAsync($"Account {account.DisplayName} already in queue, prioritizing for immediate login");

                    // Mark all other pending items as completed (acknowledged but not processed)
                    foreach (var item in queueItems)
                    {
                        if (item.Id != existingItem.Id && item.Status == AutoLoginQueueStatus.Pending)
                        {
                            originalStatuses[item.Id] = item.Status;
                            item.Status = AutoLoginQueueStatus.Completed;
                            item.StatusMessage = "Skipped - Login Now used for another account";
                            item.EndTime = DateTime.Now;
                        }
                    }

                    // Ensure the selected item is pending
                    if (existingItem.Status != AutoLoginQueueStatus.Pending)
                    {
                        existingItem.Status = AutoLoginQueueStatus.Pending;
                        existingItem.StatusMessage = "Prioritized for immediate login";
                    }
                }
                else
                {
                    // Account is not in queue, need to add it
                    await _loggingService.LogInfoAsync($"Adding {account.DisplayName} to queue for immediate login");

                    // Mark all existing pending items as completed (acknowledged but not processed)
                    foreach (var item in queueItems)
                    {
                        if (item.Status == AutoLoginQueueStatus.Pending)
                        {
                            originalStatuses[item.Id] = item.Status;
                            item.Status = AutoLoginQueueStatus.Completed;
                            item.StatusMessage = "Skipped - Login Now used for another account";
                            item.EndTime = DateTime.Now;
                        }
                    }

                    // Add the account to the queue
                    await _queueService.AddToQueueAsync(account, CurrentProfile);
                }

                // Start the queue immediately (will process only the selected/added item)
                await _queueService.StartQueueAsync();

                _statusService.SetTemporaryMessage($"Starting immediate login for {account.DisplayName} (queue preserved)", TimeSpan.FromSeconds(3));
                await _loggingService.LogInfoAsync($"Started immediate login for account {account.DisplayName} from profile {CurrentProfile.Name} - queue preserved");

                // Store the original statuses for potential future restoration
                // This could be used in a future enhancement to restore the queue after login completes
                if (originalStatuses.Count > 0)
                {
                    await _loggingService.LogDebugAsync($"Preserved {originalStatuses.Count} queue items that were temporarily skipped");
                }
            }
            catch (Exception ex)
            {
                _statusService.SetTemporaryMessage($"Failed to start immediate login for {account.DisplayName}", TimeSpan.FromSeconds(3));
                await _loggingService.LogErrorAsync($"Error starting immediate login for account: {account.DisplayName}", ex);
            }
        }

        private async Task LoginNowSelectedAsync()
        {
            if (SelectedAccount != null)
            {
                await LoginNowAsync(SelectedAccount);
            }
        }

        private async Task AddToQueueSelectedAsync()
        {
            if (SelectedAccount != null)
            {
                await AddToQueueAsync(SelectedAccount);
            }
        }

        /// <summary>
        /// Cleanup resources when ViewModel is disposed
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Stop and dispose timer
                _otpRefreshTimer?.Stop();
                _otpRefreshTimer = null;

                // Clear any visible OTP codes to ensure clean state
                if (Accounts != null)
                {
                    foreach (var account in Accounts.Where(a => a != null && a.IsOTPCodeVisible))
                    {
                        account.IsOTPCodeVisible = false;
                        account.CurrentOTPCode = null;
                        account.OTPTimeRemaining = 100.0;
                    }
                }

                _disposed = true;
            }
        }

        #endregion

        #region Helper Methods

        private int GetNextAvailableSlot()
        {
            for (int slot = 1; slot <= 4; slot++)
            {
                if (!Accounts.Any(a => a.POLMemberSlot == slot))
                {
                    return slot;
                }
            }
            return 1; // Default if somehow all slots are taken
        }

        private void UpdateAccountsHeader()
        {
            if (CurrentProfile == null)
            {
                AccountsHeader = "PlayOnline Member Accounts";
            }
            else if (CurrentProfile.IsSystemFile)
            {
                AccountsHeader = "PlayOnline Member Accounts (System Profile - Read Only)";
            }
            else
            {
                AccountsHeader = $"PlayOnline Member Accounts - {CurrentProfile.Name}";
            }
        }

        private void UpdateCommandStates()
        {
            if (AddAccountCommand is RelayCommand addCmd)
                addCmd.RaiseCanExecuteChanged();
            if (EditAccountCommand is RelayCommand editCmd)
                editCmd.RaiseCanExecuteChanged();
            if (DeleteAccountCommand is RelayCommand deleteCmd)
                deleteCmd.RaiseCanExecuteChanged();
            if (RecoverCredentialsCommand is RelayCommand recoverCmd)
                recoverCmd.RaiseCanExecuteChanged();
        }

        #endregion
    }

    /// <summary>
    /// ViewModel for the account edit dialog
    /// </summary>
    public class PlayOnlineMemberAccountEditViewModel : ViewModelBase
    {
        private readonly List<PlayOnlineMemberAccount> _existingAccounts;
        private readonly IWorkflowService _workflowService;
        private string _authenticationKeyInput = string.Empty;
        private WorkflowDefinition? _selectedWorkflow;

        public PlayOnlineMemberAccountEditViewModel(
            PlayOnlineMemberAccount account,
            List<PlayOnlineMemberAccount> existingAccounts,
            IWorkflowService workflowService)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
            _existingAccounts = existingAccounts ?? new List<PlayOnlineMemberAccount>();
            _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));

            // Ensure OTP configuration exists
            if (Account.OTPConfiguration == null)
            {
                Account.OTPConfiguration = new OTPConfiguration();
            }

            Account.PropertyChanged += Account_PropertyChanged;

            // Load available workflows asynchronously
            _ = LoadWorkflowsAsync();
        }

        public PlayOnlineMemberAccount Account { get; }

        public bool IsEditMode => Account.Id != Guid.Empty;

        public string DialogTitle => IsEditMode ? "Edit PlayOnline Member Account" : "Add PlayOnline Member Account";

        /// <summary>
        /// Authentication key input for OTP configuration
        /// </summary>
        public string AuthenticationKeyInput
        {
            get => _authenticationKeyInput;
            set => SetProperty(ref _authenticationKeyInput, value);
        }

        /// <summary>
        /// Available POL Member Slots (1-4)
        /// </summary>
        public int[] AvailablePOLSlots => new[] { 1, 2, 3, 4 };

        /// <summary>
        /// Available FFXI Character Slots (1-16)
        /// </summary>
        public int[] AvailableFFXISlots => Enumerable.Range(1, 16).ToArray();

        /// <summary>
        /// Available workflows for selection
        /// </summary>
        public ObservableCollection<WorkflowDefinition> AvailableWorkflows { get; } = new();

        /// <summary>
        /// Currently selected workflow (null = use default workflow)
        /// </summary>
        public WorkflowDefinition? SelectedWorkflow
        {
            get => _selectedWorkflow;
            set
            {
                if (SetProperty(ref _selectedWorkflow, value))
                {
                    // Update account's WorkflowId when selection changes
                    Account.WorkflowId = value?.WorkflowId;
                }
            }
        }

        /// <summary>
        /// Checks if a POL slot is already in use
        /// </summary>
        public bool IsPOLSlotAvailable(int slot)
        {
            return !_existingAccounts.Any(a => a.POLMemberSlot == slot);
        }

        private void Account_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Re-validate when properties change
            OnPropertyChanged(nameof(Account));
        }

        /// <summary>
        /// Loads available workflows and sets the initial selection based on account's WorkflowId
        /// </summary>
        private async Task LoadWorkflowsAsync()
        {
            try
            {
                var workflows = await _workflowService.GetAvailableWorkflowsAsync();

                // Add "Use Default Workflow" option at the top
                AvailableWorkflows.Clear();
                AvailableWorkflows.Add(new WorkflowDefinition
                {
                    WorkflowId = Guid.Empty,
                    Name = "(Use Default Workflow)",
                    Description = "Automatically use the system default workflow"
                });

                // Add all available workflows
                foreach (var workflow in workflows)
                {
                    AvailableWorkflows.Add(workflow);
                }

                // Set initial selection based on account's WorkflowId
                if (Account.WorkflowId.HasValue && Account.WorkflowId.Value != Guid.Empty)
                {
                    SelectedWorkflow = AvailableWorkflows.FirstOrDefault(w => w.WorkflowId == Account.WorkflowId.Value);
                }
                else
                {
                    // Select "Use Default Workflow" option
                    SelectedWorkflow = AvailableWorkflows.FirstOrDefault(w => w.WorkflowId == Guid.Empty);
                }
            }
            catch (Exception)
            {
                // Best-effort - if workflow loading fails, leave empty collection
                // Dialog will still be functional for other account settings
            }
        }
    }
}

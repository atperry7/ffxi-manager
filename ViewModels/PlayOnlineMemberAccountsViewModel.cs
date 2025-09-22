using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;
using FFXIManager.Views;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// ViewModel for managing PlayOnline Member Account associations
    /// </summary>
    public class PlayOnlineMemberAccountsViewModel : ViewModelBase
    {
        private readonly IPlayOnlineMemberAccountService _accountService;
        private readonly IStatusMessageService _statusService;
        private readonly ILoggingService _loggingService;
        private readonly IDialogService _dialogService;
        private readonly IUiDispatcher _uiDispatcher;

        private ProfileInfo? _currentProfile;
        private PlayOnlineMemberAccount? _selectedAccount;
        private bool _isLoading;
        private string _accountsHeader = "PlayOnline Member Accounts";

        public PlayOnlineMemberAccountsViewModel(
            IPlayOnlineMemberAccountService accountService,
            IStatusMessageService statusService,
            ILoggingService loggingService,
            IDialogService dialogService,
            IUiDispatcher uiDispatcher)
        {
            _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));

            Accounts = new ObservableCollection<PlayOnlineMemberAccount>();
            Accounts.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(CanAddAccount));
                UpdateCommandStates();
            };
            InitializeCommands();
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

        // Parameter-based commands for context menu
        public ICommand EditAccountParameterCommand { get; private set; } = null!;
        public ICommand DeleteAccountParameterCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            AddAccountCommand = new RelayCommand(async () => await AddAccountAsync(), () => CanAddAccount);
            EditAccountCommand = new RelayCommand(async () => await EditSelectedAccountAsync(), () => SelectedAccount != null);
            DeleteAccountCommand = new RelayCommand(async () => await DeleteSelectedAccountAsync(), () => SelectedAccount != null);
            RefreshCommand = new RelayCommand(async () => await RefreshAccountsAsync());

            EditAccountParameterCommand = new RelayCommandWithParameter<PlayOnlineMemberAccount>(async account => await EditAccountAsync(account));
            DeleteAccountParameterCommand = new RelayCommandWithParameter<PlayOnlineMemberAccount>(async account => await DeleteAccountAsync(account));
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
                DataContext = new PlayOnlineMemberAccountEditViewModel(newAccount, Accounts.ToList())
            };

            if (dialog.ShowDialog() == true)
            {
                var editVm = (PlayOnlineMemberAccountEditViewModel)dialog.DataContext;
                var success = await _accountService.AddAccountAsync(CurrentProfile.FilePath, editVm.Account);

                if (success)
                {
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
                POLPassword = account.POLPassword,
                OTPConfiguration = account.OTPConfiguration != null
                    ? new OTPConfiguration { IsEnabled = account.OTPConfiguration.IsEnabled }
                    : null
            };

            var dialog = new PlayOnlineMemberAccountEditDialog
            {
                DataContext = new PlayOnlineMemberAccountEditViewModel(
                    editAccount,
                    Accounts.Where(a => a.Id != account.Id).ToList())
            };

            if (dialog.ShowDialog() == true)
            {
                var editVm = (PlayOnlineMemberAccountEditViewModel)dialog.DataContext;
                var success = await _accountService.UpdateAccountAsync(CurrentProfile.FilePath, editVm.Account);

                if (success)
                {
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
                    await RefreshAccountsAsync();
                    _statusService.SetTemporaryMessage($"Deleted account: {account.DisplayName}", TimeSpan.FromSeconds(3));
                }
                else
                {
                    _statusService.SetTemporaryMessage("Failed to delete account", TimeSpan.FromSeconds(3));
                }
            }
        }

        private async Task RefreshAccountsAsync()
        {
            if (CurrentProfile == null || CurrentProfile.IsSystemFile)
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
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
        }

        #endregion
    }

    /// <summary>
    /// ViewModel for the account edit dialog
    /// </summary>
    public class PlayOnlineMemberAccountEditViewModel : ViewModelBase
    {
        private readonly List<PlayOnlineMemberAccount> _existingAccounts;

        public PlayOnlineMemberAccountEditViewModel(
            PlayOnlineMemberAccount account,
            List<PlayOnlineMemberAccount> existingAccounts)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
            _existingAccounts = existingAccounts ?? new List<PlayOnlineMemberAccount>();

            // Ensure OTP configuration exists
            if (Account.OTPConfiguration == null)
            {
                Account.OTPConfiguration = new OTPConfiguration();
            }

            Account.PropertyChanged += Account_PropertyChanged;
        }

        public PlayOnlineMemberAccount Account { get; }

        public bool IsEditMode => Account.Id != Guid.Empty;

        public string DialogTitle => IsEditMode ? "Edit PlayOnline Member Account" : "Add PlayOnline Member Account";

        /// <summary>
        /// Available POL Member Slots (1-4)
        /// </summary>
        public int[] AvailablePOLSlots => new[] { 1, 2, 3, 4 };

        /// <summary>
        /// Available FFXI Character Slots (1-16)
        /// </summary>
        public int[] AvailableFFXISlots => Enumerable.Range(1, 16).ToArray();

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
    }
}
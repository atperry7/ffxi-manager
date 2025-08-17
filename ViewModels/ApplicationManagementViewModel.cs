using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;
using FFXIManager.Views;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using FFXIManager.Infrastructure;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// ViewModel responsible for Application Management operations
    /// </summary>
    public class ApplicationManagementViewModel : ViewModelBase
    {
        private readonly IExternalApplicationService _applicationService;
        private readonly IStatusMessageService _statusService;
        private readonly ILoggingService _loggingService;
        private bool _isBusy;

        public ApplicationManagementViewModel(
            IExternalApplicationService applicationService,
            IStatusMessageService statusService,
            ILoggingService loggingService)
        {
            _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            ExternalApplications = new ObservableCollection<ExternalApplication>();

            _applicationService.ApplicationStatusChanged += OnApplicationStatusChanged;
            InitializeCommands();

            // Start monitoring
            _applicationService.StartMonitoring();
        }

        #region Properties

        public ObservableCollection<ExternalApplication> ExternalApplications { get; }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        #endregion

        #region Commands

        public ICommand LaunchApplicationCommand { get; private set; } = null!;
        public ICommand KillApplicationCommand { get; private set; } = null!;
        public ICommand EditApplicationCommand { get; private set; } = null!;
        public ICommand RemoveApplicationCommand { get; private set; } = null!;
        public ICommand AddApplicationCommand { get; private set; } = null!;
        public ICommand RefreshApplicationsCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            LaunchApplicationCommand = new RelayCommandWithParameter<ExternalApplication>(
                async app => await LaunchApplicationAsync(app),
                app => app != null && app.IsEnabled);
            KillApplicationCommand = new RelayCommandWithParameter<ExternalApplication>(
                async app => await KillApplicationAsync(app),
                app => app != null && app.IsRunning);
            EditApplicationCommand = new RelayCommandWithParameter<ExternalApplication>(
                async app => await EditApplicationAsync(app),
                app => app != null);
            RemoveApplicationCommand = new RelayCommandWithParameter<ExternalApplication>(
                async app => await RemoveApplicationAsync(app),
                app => app != null);
            AddApplicationCommand = new RelayCommand(async () => await AddApplicationAsync());
            RefreshApplicationsCommand = new RelayCommand(async () => await LoadExternalApplicationsAsync());
        }

        #endregion

        #region Public Methods

        public async Task LoadExternalApplicationsAsync()
        {
            try
            {
                IsBusy = true;
                _statusService.SetMessage("Loading external applications...");

                var applications = await _applicationService.GetApplicationsAsync();
                await _applicationService.RefreshApplicationStatusAsync();

                await ServiceLocator.UiDispatcher.InvokeAsync(() =>
                {
                    ExternalApplications.Clear();
                    foreach (var app in applications)
                    {
                        ExternalApplications.Add(app);
                    }
                });

                _statusService.SetMessage($"Loaded {applications.Count} external applications");
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error loading applications: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Private Methods

        private async Task LaunchApplicationAsync(ExternalApplication application)
        {
            if (application == null) return;

            try
            {
                IsBusy = true;
                _statusService.SetMessage($"Launching {application.Name}...");

                if (!application.ExecutableExists)
                {
                    var result = MessageBox.Show(
                        $"The executable for '{application.Name}' was not found at:\n{application.ExecutablePath}\n\nWould you like to configure the correct path?",
                        "Executable Not Found",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        await EditApplicationAsync(application);
                        return;
                    }
                    else
                    {
                        _statusService.SetMessage($"Launch cancelled - {application.Name} executable not found");
                        return;
                    }
                }

                var success = await _applicationService.LaunchApplicationAsync(application);

                if (success)
                {
                    _statusService.SetMessage($"Successfully launched {application.Name}");
                }
                else
                {
                    _statusService.SetMessage($"Failed to launch {application.Name}");
                }
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error launching {application.Name}: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task KillApplicationAsync(ExternalApplication application)
        {
            if (application == null) return;

            try
            {
                IsBusy = true;
                _statusService.SetMessage($"Stopping {application.Name}...");

                var success = await _applicationService.KillApplicationAsync(application);

                if (success)
                {
                    _statusService.SetMessage($"Successfully stopped {application.Name}");
                }
                else
                {
                    _statusService.SetMessage($"Failed to stop {application.Name}");
                }
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error stopping {application.Name}: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task EditApplicationAsync(ExternalApplication application)
        {
            if (application == null)
            {
                _statusService.SetMessage("No application selected for editing");
                return;
            }

            try
            {
                IsBusy = true;
                _statusService.SetMessage($"Opening configuration for {application.Name}...");

                // Create and show dialog on UI thread
                var dialogResult = await ServiceLocator.UiDispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var dialog = new ApplicationConfigDialog(application)
                        {
                            Owner = Application.Current.MainWindow
                        };
                        return dialog.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error opening configuration dialog: {ex.Message}", "Dialog Error",
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                        _statusService.SetMessage($"Failed to open configuration dialog: {ex.Message}");
                        return (bool?)false;
                    }
                });

                // Process result
                if (dialogResult == true)
                {
                    // IMPORTANT: Call UpdateApplicationAsync to persist changes
                    await _applicationService.UpdateApplicationAsync(application);

                    _statusService.SetMessage($"Application {application.Name} updated successfully");

                    // Trigger property notifications for UI updates
                    application.OnPropertyChanged(nameof(application.StatusColor));
                    application.OnPropertyChanged(nameof(application.StatusText));
                    application.OnPropertyChanged(nameof(application.ExecutableExists));
                }
                else
                {
                    _statusService.SetMessage("Configuration cancelled");
                }
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error editing application: {ex.Message}");
                await _loggingService.LogErrorAsync($"Error in EditApplicationAsync for {application.Name}", ex, "ApplicationManagementViewModel");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RemoveApplicationAsync(ExternalApplication application)
        {
            if (application == null) return;

            try
            {
                IsBusy = true;
                var result = MessageBox.Show(
                    $"Are you sure you want to remove '{application.Name}'?",
                    "Confirm Remove Application",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _applicationService.RemoveApplicationAsync(application);
                    ExternalApplications.Remove(application);
                    _statusService.SetMessage($"Removed application: {application.Name}");
                }
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error removing application: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task AddApplicationAsync()
        {
            try
            {
                IsBusy = true;
                _statusService.SetMessage("Creating new application...");

                var newApplication = new ExternalApplication
                {
                    Name = "New Application",
                    AllowMultipleInstances = false,
                    IsEnabled = true
                };

                // Create and show dialog on UI thread
                var dialogResult = await ServiceLocator.UiDispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var dialog = new ApplicationConfigDialog(newApplication)
                        {
                            Owner = Application.Current.MainWindow
                        };
                        return dialog.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error opening application dialog: {ex.Message}", "Dialog Error",
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                        _statusService.SetMessage($"Failed to open application dialog: {ex.Message}");
                        return (bool?)false;
                    }
                });

                // Process result
                if (dialogResult == true)
                {
                    await _applicationService.AddApplicationAsync(newApplication);
                    ExternalApplications.Add(newApplication);
                    _statusService.SetMessage($"Added application: {newApplication.Name}");
                }
                else
                {
                    _statusService.SetMessage("Application creation cancelled");
                }
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error adding application: {ex.Message}");
                await _loggingService.LogErrorAsync("Error in AddApplicationAsync", ex, "ApplicationManagementViewModel");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnApplicationStatusChanged(object? sender, ExternalApplication application)
        {
            // Update UI on status changes - this runs on a background thread
            ServiceLocator.UiDispatcher.BeginInvoke(() =>
            {
                // Force command CanExecute reevaluation when application status changes
                ((RelayCommandWithParameter<ExternalApplication>)KillApplicationCommand).RaiseCanExecuteChanged();
                ((RelayCommandWithParameter<ExternalApplication>)LaunchApplicationCommand).RaiseCanExecuteChanged();
                ((RelayCommandWithParameter<ExternalApplication>)EditApplicationCommand).RaiseCanExecuteChanged();
                ((RelayCommandWithParameter<ExternalApplication>)RemoveApplicationCommand).RaiseCanExecuteChanged();
            });
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.ViewModels.Base;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// ViewModel for the Workflow Editor, enabling users to create, modify, and test
    /// data-driven auto-login workflows without code changes.
    /// </summary>
    public class WorkflowEditorViewModel : ViewModelBase, IDisposable
    {
        private readonly IWorkflowService _workflowService;
        private readonly ILoggingService _loggingService;
        private readonly IDialogService _dialogService;
        private readonly IUiDispatcher _uiDispatcher;
        private readonly WorkflowTaskBuilder _taskBuilder;
        private readonly ITemplateManagementService _templateService;
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IExternalApplicationService _externalApplicationService;

        private ObservableCollection<WorkflowDefinition> _workflows;
        private ObservableCollection<ExternalApplication> _availableApplications;
        private WorkflowDefinition? _selectedWorkflow;
        private WorkflowStepDefinition? _selectedStep;
        private KeyboardAction? _selectedNavigationAction;
        private BitmapImage? _templateImageSource;
        private bool _isLoading;
        private bool _hasUnsavedChanges;
        private bool _disposed;
        private CancellationTokenSource _cancellationTokenSource = new();

        // Event subscription tracking to prevent memory leaks
        private WorkflowDefinition? _subscribedWorkflow;
        private WorkflowStepDefinition? _subscribedStep;
        private NavigationAction? _subscribedNavigation;
        private ObservableCollection<KeyboardAction>? _subscribedSequence;
        private readonly List<KeyboardAction> _subscribedActions = new();

        public WorkflowEditorViewModel(
            IWorkflowService workflowService,
            ILoggingService loggingService,
            IDialogService dialogService,
            IUiDispatcher uiDispatcher,
            WorkflowTaskBuilder taskBuilder,
            ITemplateManagementService templateService,
            IScreenshotCaptureService screenshotService,
            IServiceProvider serviceProvider,
            IExternalApplicationService externalApplicationService)
        {
            _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            _taskBuilder = taskBuilder ?? throw new ArgumentNullException(nameof(taskBuilder));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));

            _workflows = new ObservableCollection<WorkflowDefinition>();
            _availableApplications = new ObservableCollection<ExternalApplication>();

            InitializeCommands();
            _ = LoadDataAsync();
        }

        #region Properties

        /// <summary>
        /// Collection of all available workflows
        /// </summary>
        public ObservableCollection<WorkflowDefinition> Workflows
        {
            get => _workflows;
            set => SetProperty(ref _workflows, value);
        }

        /// <summary>
        /// Currently selected workflow for editing
        /// </summary>
        public WorkflowDefinition? SelectedWorkflow
        {
            get => _selectedWorkflow;
            set
            {
                if (SetProperty(ref _selectedWorkflow, value))
                {
                    // Unsubscribe from previous workflow
                    UnsubscribeFromWorkflowChanges();

                    SelectedStep = null;
                    OnPropertyChanged(nameof(HasWorkflowSelected));
                    OnPropertyChanged(nameof(CanEditWorkflow));
                    OnPropertyChanged(nameof(WorkflowSteps));

                    // Subscribe to new workflow
                    SubscribeToWorkflowChanges();

                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Currently selected step within the workflow
        /// </summary>
        public WorkflowStepDefinition? SelectedStep
        {
            get => _selectedStep;
            set
            {
                if (SetProperty(ref _selectedStep, value))
                {
                    // Unsubscribe from previous step
                    UnsubscribeFromStepChanges();

                    OnPropertyChanged(nameof(HasStepSelected));
                    OnPropertyChanged(nameof(CanEditStep));
                    OnPropertyChanged(nameof(NavigationActions));
                    OnPropertyChanged(nameof(HasNavigationAction));
                    SelectedNavigationAction = null;

                    // Load template image preview
                    LoadTemplateImage();

                    // Subscribe to new step
                    SubscribeToStepChanges();

                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Whether data is being loaded
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Whether there are unsaved changes
        /// </summary>
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                if (SetProperty(ref _hasUnsavedChanges, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Whether a workflow is selected
        /// </summary>
        public bool HasWorkflowSelected => SelectedWorkflow != null;

        /// <summary>
        /// Whether the selected workflow can be edited
        /// </summary>
        public bool CanEditWorkflow => SelectedWorkflow != null && !SelectedWorkflow.IsReadOnly;

        /// <summary>
        /// Whether a step is selected
        /// </summary>
        public bool HasStepSelected => SelectedStep != null;

        /// <summary>
        /// Whether the selected step can be edited
        /// </summary>
        public bool CanEditStep => SelectedStep != null && CanEditWorkflow;

        /// <summary>
        /// Steps from the currently selected workflow
        /// </summary>
        public ObservableCollection<WorkflowStepDefinition>? WorkflowSteps => SelectedWorkflow?.Steps;

        /// <summary>
        /// Available external applications for dropdown binding in step editor
        /// </summary>
        public ObservableCollection<ExternalApplication> AvailableApplications
        {
            get => _availableApplications;
            set => SetProperty(ref _availableApplications, value);
        }

        /// <summary>
        /// Currently selected navigation action within the step's navigation sequence
        /// </summary>
        public KeyboardAction? SelectedNavigationAction
        {
            get => _selectedNavigationAction;
            set
            {
                if (SetProperty(ref _selectedNavigationAction, value))
                {
                    OnPropertyChanged(nameof(HasNavigationActionSelected));
                    OnPropertyChanged(nameof(CanEditNavigationAction));
                    OnPropertyChanged(nameof(IsLaunchActionSelected));
                    OnPropertyChanged(nameof(IsWaitActionSelected));
                    OnPropertyChanged(nameof(IsInputPasswordActionSelected));
                    OnPropertyChanged(nameof(IsInputOTPActionSelected));
                    OnPropertyChanged(nameof(IsMemberSlotActionSelected));
                    OnPropertyChanged(nameof(IsCharacterSlotActionSelected));
                    OnPropertyChanged(nameof(IsSlotActionSelected));
                    OnPropertyChanged(nameof(LaunchApplicationName));
                    OnPropertyChanged(nameof(LaunchAllowSkipIfRunning));
                    OnPropertyChanged(nameof(LaunchAllowSkipIfNotConfigured));
                    OnPropertyChanged(nameof(SlotNavigationMethod));
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Navigation actions for the currently selected step
        /// </summary>
        public ObservableCollection<KeyboardAction>? NavigationActions => SelectedStep?.Navigation?.Sequence;

        /// <summary>
        /// Whether the selected step has a navigation action defined
        /// </summary>
        public bool HasNavigationAction => SelectedStep?.Navigation != null;

        /// <summary>
        /// Whether a navigation action is selected
        /// </summary>
        public bool HasNavigationActionSelected => SelectedNavigationAction != null;

        /// <summary>
        /// Whether the selected navigation action can be edited
        /// </summary>
        public bool CanEditNavigationAction => SelectedNavigationAction != null && CanEditStep;

        /// <summary>
        /// Whether the selected navigation action is a Launch action
        /// </summary>
        public bool IsLaunchActionSelected => SelectedNavigationAction?.Action == "Launch";

        /// <summary>
        /// Whether the selected navigation action is a Wait action
        /// </summary>
        public bool IsWaitActionSelected => SelectedNavigationAction?.Action == "Wait";

        /// <summary>
        /// Whether the selected navigation action is an InputPassword action
        /// </summary>
        public bool IsInputPasswordActionSelected => SelectedNavigationAction?.Action == "InputPassword";

        /// <summary>
        /// Whether the selected navigation action is an InputOTP action
        /// </summary>
        public bool IsInputOTPActionSelected => SelectedNavigationAction?.Action == "InputOTP";

        /// <summary>
        /// Whether the selected navigation action is a MemberSlot action
        /// </summary>
        public bool IsMemberSlotActionSelected => SelectedNavigationAction?.Action == "MemberSlot";

        /// <summary>
        /// Whether the selected navigation action is a CharacterSlot action
        /// </summary>
        public bool IsCharacterSlotActionSelected => SelectedNavigationAction?.Action == "CharacterSlot";

        /// <summary>
        /// Whether the selected navigation action is a slot-based action (MemberSlot or CharacterSlot)
        /// </summary>
        public bool IsSlotActionSelected => IsMemberSlotActionSelected || IsCharacterSlotActionSelected;

        /// <summary>
        /// Navigation method for slot actions (from Parameters dictionary)
        /// </summary>
        public string SlotNavigationMethod
        {
            get => SelectedNavigationAction?.GetParameter<string>("NavigationMethod", "Keyboard") ?? "Keyboard";
            set
            {
                if (SelectedNavigationAction != null)
                {
                    SelectedNavigationAction.SetParameter("NavigationMethod", value);
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        /// <summary>
        /// Template image source for thumbnail preview
        /// </summary>
        public BitmapImage? TemplateImageSource
        {
            get => _templateImageSource;
            set
            {
                if (SetProperty(ref _templateImageSource, value))
                {
                    OnPropertyChanged(nameof(HasTemplateImage));
                }
            }
        }

        /// <summary>
        /// Whether the selected step has a template image
        /// </summary>
        public bool HasTemplateImage => TemplateImageSource != null;

        /// <summary>
        /// Application name for Launch actions (from Parameters dictionary)
        /// </summary>
        public string? LaunchApplicationName
        {
            get => SelectedNavigationAction?.GetParameter<string?>("ApplicationName", null);
            set
            {
                if (SelectedNavigationAction != null && value != null)
                {
                    SelectedNavigationAction.SetParameter("ApplicationName", value);
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        /// <summary>
        /// Skip if running flag for Launch actions (from Parameters dictionary)
        /// </summary>
        public bool LaunchAllowSkipIfRunning
        {
            get => SelectedNavigationAction?.GetParameter<bool>("AllowSkipIfRunning", false) ?? false;
            set
            {
                if (SelectedNavigationAction != null)
                {
                    SelectedNavigationAction.SetParameter("AllowSkipIfRunning", value);
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        /// <summary>
        /// Skip if not configured flag for Launch actions (from Parameters dictionary)
        /// </summary>
        public bool LaunchAllowSkipIfNotConfigured
        {
            get => SelectedNavigationAction?.GetParameter<bool>("AllowSkipIfNotConfigured", true) ?? true;
            set
            {
                if (SelectedNavigationAction != null)
                {
                    SelectedNavigationAction.SetParameter("AllowSkipIfNotConfigured", value);
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        #endregion

        #region Commands

        public ICommand LoadWorkflowsCommand { get; private set; } = null!;
        public ICommand CreateNewWorkflowCommand { get; private set; } = null!;
        public ICommand SaveWorkflowCommand { get; private set; } = null!;
        public ICommand DeleteWorkflowCommand { get; private set; } = null!;
        public ICommand CloneWorkflowCommand { get; private set; } = null!;
        public ICommand ImportWorkflowCommand { get; private set; } = null!;
        public ICommand ExportWorkflowCommand { get; private set; } = null!;
        public ICommand RestoreDefaultsCommand { get; private set; } = null!;

        public ICommand AddStepCommand { get; private set; } = null!;
        public ICommand RemoveStepCommand { get; private set; } = null!;
        public ICommand MoveStepUpCommand { get; private set; } = null!;
        public ICommand MoveStepDownCommand { get; private set; } = null!;
        public ICommand EditStepCommand { get; private set; } = null!;

        public ICommand InitializeNavigationCommand { get; private set; } = null!;
        public ICommand AddNavigationActionCommand { get; private set; } = null!;
        public ICommand RemoveNavigationActionCommand { get; private set; } = null!;
        public ICommand MoveNavigationActionUpCommand { get; private set; } = null!;
        public ICommand MoveNavigationActionDownCommand { get; private set; } = null!;

        public ICommand TestWorkflowCommand { get; private set; } = null!;
        public ICommand ValidateWorkflowCommand { get; private set; } = null!;

        // Template image management commands
        public ICommand SelectTemplateImageCommand { get; private set; } = null!;
        public ICommand ReplaceTemplateImageCommand { get; private set; } = null!;
        public ICommand ShowLargeTemplateImageCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            LoadWorkflowsCommand = new RelayCommand(
                async () => await LoadWorkflowsAsync());

            CreateNewWorkflowCommand = new RelayCommand(
                async () => await CreateNewWorkflowAsync());

            SaveWorkflowCommand = new RelayCommand(
                async () => await SaveWorkflowAsync(),
                () => CanEditWorkflow && HasUnsavedChanges);

            DeleteWorkflowCommand = new RelayCommand(
                async () => await DeleteWorkflowAsync(),
                () => CanEditWorkflow);

            CloneWorkflowCommand = new RelayCommand(
                async () => await CloneWorkflowAsync(),
                () => HasWorkflowSelected);

            ImportWorkflowCommand = new RelayCommand(
                async () => await ImportWorkflowAsync());

            ExportWorkflowCommand = new RelayCommand(
                async () => await ExportWorkflowAsync(),
                () => HasWorkflowSelected);

            RestoreDefaultsCommand = new RelayCommand(
                async () => await RestoreDefaultWorkflowsAsync());

            AddStepCommand = new RelayCommand(
                async () => await AddStepAsync(),
                () => CanEditWorkflow);

            RemoveStepCommand = new RelayCommand(
                async () => await RemoveStepAsync(),
                () => CanEditStep);

            MoveStepUpCommand = new RelayCommand(
                () => MoveStepUp(),
                () => CanEditStep && SelectedStep != null && SelectedStep.Order > 0);

            MoveStepDownCommand = new RelayCommand(
                () => MoveStepDown(),
                () => CanEditStep && SelectedStep != null && SelectedWorkflow != null && SelectedStep.Order < SelectedWorkflow.Steps.Count - 1);

            EditStepCommand = new RelayCommand(
                async () => await EditStepAsync(),
                () => HasStepSelected);

            InitializeNavigationCommand = new RelayCommand(
                () => InitializeNavigation(),
                () => CanEditStep && !HasNavigationAction);

            AddNavigationActionCommand = new RelayCommand(
                () => AddNavigationAction(),
                () => CanEditStep && HasNavigationAction);

            RemoveNavigationActionCommand = new RelayCommand(
                () => RemoveNavigationAction(),
                () => CanEditNavigationAction);

            MoveNavigationActionUpCommand = new RelayCommand(
                () => MoveNavigationActionUp(),
                () => CanEditNavigationAction && SelectedNavigationAction != null && NavigationActions != null && NavigationActions.IndexOf(SelectedNavigationAction) > 0);

            MoveNavigationActionDownCommand = new RelayCommand(
                () => MoveNavigationActionDown(),
                () => CanEditNavigationAction && SelectedNavigationAction != null && NavigationActions != null && NavigationActions.IndexOf(SelectedNavigationAction) < NavigationActions.Count - 1);

            TestWorkflowCommand = new RelayCommand(
                async () => await TestWorkflowAsync(),
                () => HasWorkflowSelected);

            ValidateWorkflowCommand = new RelayCommand(
                async () => await ValidateWorkflowAsync(),
                () => HasWorkflowSelected);

            SelectTemplateImageCommand = new RelayCommand(
                async () => await SelectTemplateImageAsync(),
                () => CanEditStep);

            ReplaceTemplateImageCommand = new RelayCommand(
                async () => await ReplaceTemplateImageAsync(),
                () => CanEditStep && !string.IsNullOrEmpty(SelectedStep?.TemplatePath));

            ShowLargeTemplateImageCommand = new RelayCommand(
                async () => await ShowLargeTemplateImageAsync(),
                () => HasTemplateImage);
        }

        #endregion

        #region Command Implementations

        private async Task LoadWorkflowsAsync()
        {
            IsLoading = true;
            try
            {
                var workflows = await _workflowService.GetAvailableWorkflowsAsync(_cancellationTokenSource.Token);

                await _uiDispatcher.InvokeAsync(() =>
                {
                    Workflows.Clear();
                    foreach (var workflow in workflows.OrderBy(w => w.Name))
                    {
                        Workflows.Add(workflow);
                    }
                });

                await _loggingService.LogInfoAsync($"Loaded {workflows.Count} workflows");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error loading workflows", ex);
                await _dialogService.ShowMessageDialogAsync("Load Failed", $"Failed to load workflows: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task CreateNewWorkflowAsync()
        {
            try
            {
                var newWorkflow = new WorkflowDefinition
                {
                    WorkflowId = Guid.NewGuid(),
                    Name = "New Workflow",
                    Description = "Custom workflow created by user",
                    Version = "1.0.0",
                    IsDefault = false,
                    IsReadOnly = false,
                    CreatedDate = DateTime.UtcNow,
                    LastModifiedDate = DateTime.UtcNow,
                    Steps = new ObservableCollection<WorkflowStepDefinition>()
                };

                await _uiDispatcher.InvokeAsync(() =>
                {
                    Workflows.Add(newWorkflow);
                    SelectedWorkflow = newWorkflow;
                });

                HasUnsavedChanges = true;
                await _loggingService.LogInfoAsync("Created new workflow");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error creating workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Create Failed", $"Failed to create workflow: {ex.Message}");
            }
        }

        private async Task SaveWorkflowAsync()
        {
            if (SelectedWorkflow == null) return;

            try
            {
                // Validate before saving
                if (!_workflowService.ValidateWorkflow(SelectedWorkflow, out var errors))
                {
                    await _dialogService.ShowMessageDialogAsync("Validation Failed",
                        $"Workflow validation failed:\n{string.Join("\n", errors)}");
                    return;
                }

                // Check if this is a modified default workflow from the defaults/ directory
                // Default workflows are named by friendly names (e.g., "playonline-standard.json")
                // but saved by GUID. This creates duplicates. To prevent this, we assign a new
                // WorkflowId when a default workflow is modified, making it a distinct user workflow.
                var expectedFilePath = _workflowService.GetWorkflowFilePath(SelectedWorkflow.WorkflowId);
                var isModifiedDefault = !System.IO.File.Exists(expectedFilePath) && SelectedWorkflow.IsDefault;

                if (isModifiedDefault)
                {
                    // This is a default workflow being saved for the first time as a user workflow
                    // Assign new ID, clear default flag, update metadata
                    var oldId = SelectedWorkflow.WorkflowId;
                    var oldName = SelectedWorkflow.Name;

                    SelectedWorkflow.WorkflowId = Guid.NewGuid();
                    SelectedWorkflow.IsDefault = false;
                    SelectedWorkflow.Name = $"{SelectedWorkflow.Name} (Custom)";
                    SelectedWorkflow.CreatedDate = DateTime.UtcNow;
                    SelectedWorkflow.LastModifiedDate = DateTime.UtcNow;

                    await _loggingService.LogInfoAsync($"Converting default workflow '{oldName}' ({oldId}) to user workflow with new ID: {SelectedWorkflow.WorkflowId}");

                    // Inform user about the change
                    await _dialogService.ShowMessageDialogAsync("Workflow Converted",
                        $"The default workflow has been converted to a custom user workflow.\n\n" +
                        $"Original: {oldName}\n" +
                        $"New Name: {SelectedWorkflow.Name}\n\n" +
                        $"This prevents conflicts with the original default workflow.");
                }

                SelectedWorkflow.LastModifiedDate = DateTime.UtcNow;
                var success = await _workflowService.SaveWorkflowAsync(SelectedWorkflow, _cancellationTokenSource.Token);

                if (success)
                {
                    HasUnsavedChanges = false;

                    // Capture the saved workflow ID before reload (in case SelectedWorkflow changes)
                    var savedWorkflowId = SelectedWorkflow.WorkflowId;
                    var savedWorkflowName = SelectedWorkflow.Name;

                    // Reload workflows to reflect the new workflow in the list
                    await LoadWorkflowsAsync();

                    // Reselect the workflow by its new ID
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        var reloadedWorkflow = Workflows.FirstOrDefault(w => w != null && w.WorkflowId == savedWorkflowId);
                        if (reloadedWorkflow != null)
                        {
                            SelectedWorkflow = reloadedWorkflow;
                        }
                    });

                    await _loggingService.LogInfoAsync($"Saved workflow: {savedWorkflowName}");
                    await _dialogService.ShowMessageDialogAsync("Saved", "Workflow saved successfully");
                }
                else
                {
                    await _dialogService.ShowMessageDialogAsync("Save Failed", "Failed to save workflow");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error saving workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Save Failed", $"Failed to save workflow: {ex.Message}");
            }
        }

        private async Task DeleteWorkflowAsync()
        {
            if (SelectedWorkflow == null) return;

            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Delete Workflow",
                    $"Are you sure you want to delete '{SelectedWorkflow.Name}'?\n\nThis action cannot be undone.");

                if (result)
                {
                    var success = await _workflowService.DeleteWorkflowAsync(SelectedWorkflow.WorkflowId, _cancellationTokenSource.Token);

                    if (success)
                    {
                        await _uiDispatcher.InvokeAsync(() =>
                        {
                            Workflows.Remove(SelectedWorkflow);
                            SelectedWorkflow = null;
                        });

                        await _loggingService.LogInfoAsync("Deleted workflow");
                    }
                    else
                    {
                        await _dialogService.ShowMessageDialogAsync("Delete Failed", "Failed to delete workflow");
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error deleting workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Delete Failed", $"Failed to delete workflow: {ex.Message}");
            }
        }

        private async Task CloneWorkflowAsync()
        {
            if (SelectedWorkflow == null) return;

            try
            {
                var clonedWorkflow = await _workflowService.CloneWorkflowAsync(
                    SelectedWorkflow.WorkflowId,
                    $"{SelectedWorkflow.Name} (Copy)",
                    _cancellationTokenSource.Token);

                await _uiDispatcher.InvokeAsync(() =>
                {
                    Workflows.Add(clonedWorkflow);
                    SelectedWorkflow = clonedWorkflow;
                });

                HasUnsavedChanges = true;
                await _loggingService.LogInfoAsync($"Cloned workflow: {clonedWorkflow.Name}");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error cloning workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Clone Failed", $"Failed to clone workflow: {ex.Message}");
            }
        }

        private async Task ImportWorkflowAsync()
        {
            try
            {
                await _dialogService.ShowMessageDialogAsync("Not Implemented", "Workflow import will be implemented in a future update");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error importing workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Import Failed", $"Failed to import workflow: {ex.Message}");
            }
        }

        private async Task ExportWorkflowAsync()
        {
            if (SelectedWorkflow == null) return;

            try
            {
                await _dialogService.ShowMessageDialogAsync("Not Implemented", "Workflow export will be implemented in a future update");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error exporting workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Export Failed", $"Failed to export workflow: {ex.Message}");
            }
        }

        private async Task RestoreDefaultWorkflowsAsync()
        {
            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Restore Defaults",
                    "This will restore all default workflows from the application directory, overwriting any changes you made to defaults.\n\nAre you sure?");

                if (!result) return;

                var count = await _workflowService.RestoreDefaultWorkflowsAsync(_cancellationTokenSource.Token);

                await _loggingService.LogInfoAsync($"Restored {count} default workflows");
                await _dialogService.ShowMessageDialogAsync("Restore Complete",
                    $"Successfully restored {count} default workflow(s).\n\nReloading workflows...");

                // Reload workflows to reflect the restored defaults
                await LoadWorkflowsAsync();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error restoring defaults", ex);
                await _dialogService.ShowMessageDialogAsync("Restore Failed",
                    $"Failed to restore default workflows: {ex.Message}");
            }
        }

        private async Task AddStepAsync()
        {
            if (SelectedWorkflow == null) return;

            try
            {
                var newStep = new WorkflowStepDefinition
                {
                    StepId = Guid.NewGuid().ToString(),
                    DisplayName = "New Step",
                    Description = "Configure this step",
                    Order = SelectedWorkflow.Steps.Count,
                    IsEnabled = true,
                    IsOptional = false,
                    EstimatedDurationSeconds = 5,
                    MaxRetryAttempts = 3,
                    TemplatePath = string.Empty
                };

                await _uiDispatcher.InvokeAsync(() =>
                {
                    SelectedWorkflow.AddStep(newStep);
                    SelectedStep = newStep;
                });

                HasUnsavedChanges = true;
                OnPropertyChanged(nameof(WorkflowSteps));
                await _loggingService.LogInfoAsync("Added new step to workflow");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error adding step", ex);
                await _dialogService.ShowMessageDialogAsync("Add Failed", $"Failed to add step: {ex.Message}");
            }
        }

        private async Task RemoveStepAsync()
        {
            if (SelectedWorkflow == null || SelectedStep == null) return;

            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Remove Step",
                    $"Are you sure you want to remove '{SelectedStep.DisplayName}'?");

                if (result)
                {
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        SelectedWorkflow.RemoveStep(SelectedStep);
                        SelectedStep = null;
                    });

                    HasUnsavedChanges = true;
                    OnPropertyChanged(nameof(WorkflowSteps));
                    await _loggingService.LogInfoAsync("Removed step from workflow");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error removing step", ex);
                await _dialogService.ShowMessageDialogAsync("Remove Failed", $"Failed to remove step: {ex.Message}");
            }
        }

        private void MoveStepUp()
        {
            if (SelectedWorkflow == null || SelectedStep == null) return;

            try
            {
                SelectedWorkflow.MoveStep(SelectedStep, SelectedStep.Order - 1);
                HasUnsavedChanges = true;
                OnPropertyChanged(nameof(WorkflowSteps));
                UpdateCommandStates();
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error moving step up", ex);
            }
        }

        private void MoveStepDown()
        {
            if (SelectedWorkflow == null || SelectedStep == null) return;

            try
            {
                SelectedWorkflow.MoveStep(SelectedStep, SelectedStep.Order + 1);
                HasUnsavedChanges = true;
                OnPropertyChanged(nameof(WorkflowSteps));
                UpdateCommandStates();
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error moving step down", ex);
            }
        }

        private async Task EditStepAsync()
        {
            if (SelectedStep == null) return;

            try
            {
                // Step properties are bound directly to UI, so edits are automatic
                await _loggingService.LogDebugAsync($"Editing step: {SelectedStep.DisplayName}");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error editing step", ex);
            }
        }

        private void InitializeNavigation()
        {
            if (SelectedStep == null) return;

            try
            {
                SelectedStep.Navigation = new NavigationAction
                {
                    Description = $"Navigation for {SelectedStep.DisplayName}",
                    PostNavigationDelayMs = 500,
                    Sequence = new ObservableCollection<KeyboardAction>()
                };

                OnPropertyChanged(nameof(NavigationActions));
                OnPropertyChanged(nameof(HasNavigationAction));
                HasUnsavedChanges = true;

                // Subscribe to the newly created navigation sequence
                SubscribeToNavigationSequenceChanges();

                UpdateCommandStates();

                _ = _loggingService.LogDebugAsync($"Initialized sequence-based navigation for step: {SelectedStep.DisplayName}");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error initializing navigation", ex);
            }
        }

        private void AddNavigationAction()
        {
            if (SelectedStep?.Navigation == null) return;

            try
            {
                var newAction = new KeyboardAction
                {
                    Action = "Tab",
                    Count = 1,
                    DelayMs = 100,
                    Description = "Navigate to next field"
                };

                SelectedStep.Navigation.Sequence.Add(newAction);
                SelectedNavigationAction = newAction;

                HasUnsavedChanges = true;
                OnPropertyChanged(nameof(NavigationActions));
                UpdateCommandStates();

                _ = _loggingService.LogDebugAsync($"Added navigation action to step: {SelectedStep.DisplayName}");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error adding navigation action", ex);
            }
        }

        private void RemoveNavigationAction()
        {
            if (SelectedStep?.Navigation == null || SelectedNavigationAction == null) return;

            try
            {
                SelectedStep.Navigation.Sequence.Remove(SelectedNavigationAction);
                SelectedNavigationAction = null;

                HasUnsavedChanges = true;
                OnPropertyChanged(nameof(NavigationActions));
                UpdateCommandStates();

                _ = _loggingService.LogDebugAsync($"Removed navigation action from step: {SelectedStep.DisplayName}");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error removing navigation action", ex);
            }
        }

        private void MoveNavigationActionUp()
        {
            if (SelectedStep?.Navigation == null || SelectedNavigationAction == null) return;

            try
            {
                var sequence = SelectedStep.Navigation.Sequence;
                var index = sequence.IndexOf(SelectedNavigationAction);
                if (index <= 0) return;

                sequence.Move(index, index - 1);

                HasUnsavedChanges = true;
                UpdateCommandStates();

                _ = _loggingService.LogDebugAsync($"Moved navigation action up in step: {SelectedStep.DisplayName}");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error moving navigation action up", ex);
            }
        }

        private void MoveNavigationActionDown()
        {
            if (SelectedStep?.Navigation == null || SelectedNavigationAction == null) return;

            try
            {
                var sequence = SelectedStep.Navigation.Sequence;
                var index = sequence.IndexOf(SelectedNavigationAction);
                if (index < 0 || index >= sequence.Count - 1) return;

                sequence.Move(index, index + 1);

                HasUnsavedChanges = true;
                UpdateCommandStates();

                _ = _loggingService.LogDebugAsync($"Moved navigation action down in step: {SelectedStep.DisplayName}");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error moving navigation action down", ex);
            }
        }

        private async Task TestWorkflowAsync()
        {
            if (SelectedWorkflow == null) return;

            try
            {
                await _dialogService.ShowMessageDialogAsync("Not Implemented", "Workflow testing will be implemented in a future update");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error testing workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Test Failed", $"Failed to test workflow: {ex.Message}");
            }
        }

        private async Task ValidateWorkflowAsync()
        {
            if (SelectedWorkflow == null) return;

            try
            {
                if (_workflowService.ValidateWorkflow(SelectedWorkflow, out var errors))
                {
                    await _dialogService.ShowMessageDialogAsync("Validation Success",
                        $"Workflow '{SelectedWorkflow.Name}' is valid\n\nSteps: {SelectedWorkflow.Steps.Count}");
                }
                else
                {
                    await _dialogService.ShowMessageDialogAsync("Validation Failed",
                        $"Workflow validation failed:\n\n{string.Join("\n", errors)}");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error validating workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Validation Error", $"Failed to validate workflow: {ex.Message}");
            }
        }

        /// <summary>
        /// Opens file dialog to select an image and opens the image cropper to create a new template
        /// </summary>
        private async Task SelectTemplateImageAsync()
        {
            if (SelectedStep == null) return;

            try
            {
                // Open file dialog to select image
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Screenshot or Image for Template",
                    Filter = "Image Files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|All Files (*.*)|*.*",
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true)
                    return;

                await _loggingService.LogInfoAsync($"Selected image file: {dialog.FileName}");

                // Open image cropper dialog
                var cropViewModel = new ImageCropperDialogViewModel(dialog.FileName, _loggingService);
                var cropDialog = new Views.ImageCropperDialog(cropViewModel);
                cropDialog.Owner = System.Windows.Application.Current.MainWindow;

                var dialogResult = cropDialog.ShowDialog();

                if (dialogResult != true)
                {
                    await _loggingService.LogInfoAsync("Image selection cancelled");
                    return;
                }

                // Validate crop rectangle
                if (cropViewModel.CropRectangle == null)
                {
                    await _loggingService.LogWarningAsync("No crop rectangle selected");
                    await _dialogService.ShowMessageDialogAsync("Invalid Selection", "No crop area was selected.");
                    return;
                }

                // Generate unique template name based on step ID (flat structure - no directories)
                var templateName = $"step_{SelectedStep.StepId.ToLowerInvariant().Replace("-", "_")}";

                // Get template directory path
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = System.IO.Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                System.IO.Directory.CreateDirectory(templatesPath);

                var templateFilePath = System.IO.Path.Combine(templatesPath, $"{templateName}.png");

                await _loggingService.LogInfoAsync($"Creating template: {templateName} for step '{SelectedStep.DisplayName}'");

                // Get image crop service from DI
                var imageCropService = _serviceProvider.GetService(typeof(IImageCropService))
                    as IImageCropService;

                if (imageCropService == null)
                {
                    await _loggingService.LogErrorAsync("IImageCropService not available from DI");
                    await _dialogService.ShowMessageDialogAsync("Service Error", "Image crop service not available.");
                    return;
                }

                // Crop and save the image directly to template location
                var success = await imageCropService.CropImageAsync(
                    dialog.FileName,
                    cropViewModel.CropRectangle.Value,
                    templateFilePath);

                if (success)
                {
                    // Update step with new template path (stored without extension)
                    SelectedStep.TemplatePath = templateName;
                    HasUnsavedChanges = true;
                    UpdateCommandStates();

                    await _loggingService.LogInfoAsync($"Template created successfully: {templateName}");
                    await _dialogService.ShowMessageDialogAsync("Template Created",
                        $"Template '{templateName}' created successfully");
                }
                else
                {
                    await _dialogService.ShowMessageDialogAsync("Template Creation Failed",
                        "Failed to create template image. Check logs for details.");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error selecting template image", ex);
                await _dialogService.ShowMessageDialogAsync("Selection Failed",
                    $"Failed to select template image: {ex.Message}");
            }
        }

        /// <summary>
        /// Replaces the existing template image for the step
        /// </summary>
        private async Task ReplaceTemplateImageAsync()
        {
            if (SelectedStep == null || string.IsNullOrEmpty(SelectedStep.TemplatePath))
                return;

            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Replace Template Image",
                    $"Replace the template image for '{SelectedStep.DisplayName}'?\n\nThis will update: {SelectedStep.TemplatePath}");

                if (!result)
                    return;

                // Open file dialog to select new image
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select New Image for Template",
                    Filter = "Image Files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|All Files (*.*)|*.*",
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true)
                    return;

                await _loggingService.LogInfoAsync($"Selected replacement image: {dialog.FileName}");

                // Open image cropper dialog
                var cropViewModel = new ImageCropperDialogViewModel(dialog.FileName, _loggingService);
                var cropDialog = new Views.ImageCropperDialog(cropViewModel);
                cropDialog.Owner = System.Windows.Application.Current.MainWindow;

                var dialogResult = cropDialog.ShowDialog();

                if (dialogResult != true)
                {
                    await _loggingService.LogInfoAsync("Image replacement cancelled");
                    return;
                }

                await _loggingService.LogInfoAsync($"Replacing template: {SelectedStep.TemplatePath}");

                // Replace template image with cropped version
                var success = await _templateService.CropAndReplaceTemplateImageAsync(
                    SelectedStep.TemplatePath,
                    dialog.FileName,
                    cropViewModel.CropRectangle);

                if (success)
                {
                    await _loggingService.LogInfoAsync($"Template replaced successfully: {SelectedStep.TemplatePath}");
                    await _dialogService.ShowMessageDialogAsync("Template Replaced",
                        $"Template image for '{SelectedStep.DisplayName}' replaced successfully");
                }
                else
                {
                    await _dialogService.ShowMessageDialogAsync("Template Replacement Failed",
                        "Failed to replace template image. Check logs for details.");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error replacing template image", ex);
                await _dialogService.ShowMessageDialogAsync("Replacement Failed",
                    $"Failed to replace template image: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private async Task LoadDataAsync()
        {
            await LoadWorkflowsAsync();
            await LoadAvailableApplicationsAsync();
        }

        private async Task LoadAvailableApplicationsAsync()
        {
            try
            {
                var applications = await _externalApplicationService.GetApplicationsAsync();

                await _uiDispatcher.InvokeAsync(() =>
                {
                    AvailableApplications.Clear();
                    foreach (var app in applications.OrderBy(a => a.Name))
                    {
                        AvailableApplications.Add(app);
                    }
                });

                await _loggingService.LogDebugAsync($"Loaded {applications.Count} external applications for workflow editor");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error loading external applications", ex);
                // Don't show error to user - applications are optional for workflow editing
            }
        }

        private void UpdateCommandStates()
        {
            (SaveWorkflowCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteWorkflowCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CloneWorkflowCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportWorkflowCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RestoreDefaultsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddStepCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveStepCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveStepUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveStepDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (EditStepCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InitializeNavigationCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddNavigationActionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveNavigationActionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveNavigationActionUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveNavigationActionDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (TestWorkflowCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ValidateWorkflowCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SelectTemplateImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ReplaceTemplateImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowLargeTemplateImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Subscribes to property changes on the currently selected workflow
        /// </summary>
        private void SubscribeToWorkflowChanges()
        {
            if (_selectedWorkflow == null) return;

            // Subscribe to workflow property changes
            if (_selectedWorkflow is INotifyPropertyChanged workflowNotifier)
            {
                workflowNotifier.PropertyChanged += OnWorkflowPropertyChanged;
                _subscribedWorkflow = _selectedWorkflow;
            }
        }

        /// <summary>
        /// Unsubscribes from property changes on the previously selected workflow
        /// </summary>
        private void UnsubscribeFromWorkflowChanges()
        {
            // Unsubscribe from workflow property changes
            if (_subscribedWorkflow is INotifyPropertyChanged workflowNotifier)
            {
                workflowNotifier.PropertyChanged -= OnWorkflowPropertyChanged;
                _subscribedWorkflow = null;
            }
        }

        /// <summary>
        /// Subscribes to property changes on the currently selected step and its navigation sequence
        /// </summary>
        private void SubscribeToStepChanges()
        {
            if (_selectedStep == null) return;

            // Subscribe to step property changes
            if (_selectedStep is INotifyPropertyChanged stepNotifier)
            {
                stepNotifier.PropertyChanged += OnStepPropertyChanged;
                _subscribedStep = _selectedStep;
            }

            // Subscribe to navigation property changes
            if (_selectedStep.Navigation is INotifyPropertyChanged navigationNotifier)
            {
                navigationNotifier.PropertyChanged += OnNavigationPropertyChanged;
                _subscribedNavigation = _selectedStep.Navigation;
            }

            // Subscribe to navigation sequence changes
            SubscribeToNavigationSequenceChanges();
        }

        /// <summary>
        /// Unsubscribes from property changes on the previously selected step
        /// </summary>
        private void UnsubscribeFromStepChanges()
        {
            // Unsubscribe from step property changes
            if (_subscribedStep is INotifyPropertyChanged stepNotifier)
            {
                stepNotifier.PropertyChanged -= OnStepPropertyChanged;
                _subscribedStep = null;
            }

            // Unsubscribe from navigation property changes
            if (_subscribedNavigation is INotifyPropertyChanged navigationNotifier)
            {
                navigationNotifier.PropertyChanged -= OnNavigationPropertyChanged;
                _subscribedNavigation = null;
            }

            // Unsubscribe from navigation sequence changes
            UnsubscribeFromNavigationSequenceChanges();
        }

        /// <summary>
        /// Subscribes to collection changes on the navigation sequence and property changes on individual actions
        /// </summary>
        private void SubscribeToNavigationSequenceChanges()
        {
            if (_selectedStep?.Navigation?.Sequence == null) return;

            var sequence = _selectedStep.Navigation.Sequence;

            // Subscribe to collection changes
            sequence.CollectionChanged += OnNavigationSequenceChanged;
            _subscribedSequence = sequence;

            // Subscribe to property changes on existing actions
            foreach (var action in sequence)
            {
                SubscribeToActionPropertyChanges(action);
            }
        }

        /// <summary>
        /// Unsubscribes from navigation sequence collection changes and all action property changes
        /// </summary>
        private void UnsubscribeFromNavigationSequenceChanges()
        {
            // Unsubscribe from collection changes
            if (_subscribedSequence != null)
            {
                _subscribedSequence.CollectionChanged -= OnNavigationSequenceChanged;
                _subscribedSequence = null;
            }

            // Unsubscribe from all action property changes
            foreach (var action in _subscribedActions.ToList())
            {
                UnsubscribeFromActionPropertyChanges(action);
            }
        }

        /// <summary>
        /// Subscribes to property changes on a navigation action
        /// </summary>
        private void SubscribeToActionPropertyChanges(KeyboardAction action)
        {
            if (action == null) return;

            action.PropertyChanged += OnActionPropertyChanged;
            _subscribedActions.Add(action);
        }

        /// <summary>
        /// Unsubscribes from property changes on a navigation action
        /// </summary>
        private void UnsubscribeFromActionPropertyChanges(KeyboardAction action)
        {
            if (action == null) return;

            action.PropertyChanged -= OnActionPropertyChanged;
            _subscribedActions.Remove(action);
        }

        /// <summary>
        /// Event handler for workflow property changes
        /// </summary>
        private void OnWorkflowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Any property change on the workflow means unsaved changes
            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Event handler for step property changes
        /// </summary>
        private void OnStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Any property change on the step means unsaved changes
            HasUnsavedChanges = true;

            // Reload template image if TemplatePath changed
            if (e.PropertyName == nameof(WorkflowStepDefinition.TemplatePath))
            {
                LoadTemplateImage();
            }
        }

        /// <summary>
        /// Event handler for navigation property changes (Description, PostNavigationDelayMs)
        /// </summary>
        private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Any property change on navigation means unsaved changes
            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Event handler for navigation sequence collection changes
        /// </summary>
        private void OnNavigationSequenceChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Handle new items being added
            if (e.NewItems != null)
            {
                foreach (KeyboardAction action in e.NewItems)
                {
                    SubscribeToActionPropertyChanges(action);
                }
            }

            // Handle items being removed
            if (e.OldItems != null)
            {
                foreach (KeyboardAction action in e.OldItems)
                {
                    UnsubscribeFromActionPropertyChanges(action);
                }
            }

            // Collection changes already set HasUnsavedChanges in existing code
        }

        /// <summary>
        /// Event handler for action property changes
        /// </summary>
        private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Any property change on a navigation action means unsaved changes
            HasUnsavedChanges = true;

            // If the Action property changed, notify the UI to update visibility of detail panels
            if (e.PropertyName == nameof(KeyboardAction.Action) && sender == SelectedNavigationAction)
            {
                OnPropertyChanged(nameof(IsLaunchActionSelected));
                OnPropertyChanged(nameof(IsWaitActionSelected));
                OnPropertyChanged(nameof(IsInputPasswordActionSelected));
                OnPropertyChanged(nameof(IsInputOTPActionSelected));
                OnPropertyChanged(nameof(IsMemberSlotActionSelected));
                OnPropertyChanged(nameof(IsCharacterSlotActionSelected));
                OnPropertyChanged(nameof(IsSlotActionSelected));
                OnPropertyChanged(nameof(LaunchApplicationName));
                OnPropertyChanged(nameof(LaunchAllowSkipIfRunning));
                OnPropertyChanged(nameof(LaunchAllowSkipIfNotConfigured));
                OnPropertyChanged(nameof(SlotNavigationMethod));
            }
        }

        /// <summary>
        /// Loads the template image for the currently selected step
        /// </summary>
        private void LoadTemplateImage()
        {
            try
            {
                // Clear previous image
                TemplateImageSource = null;
                OnPropertyChanged(nameof(HasTemplateImage));

                if (SelectedStep == null || string.IsNullOrWhiteSpace(SelectedStep.TemplatePath))
                    return;

                // Construct template file path
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = System.IO.Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFileName = SelectedStep.TemplatePath.EndsWith(".png")
                    ? SelectedStep.TemplatePath
                    : $"{SelectedStep.TemplatePath}.png";
                var templateFilePath = System.IO.Path.Combine(templatesPath, templateFileName);

                if (!System.IO.File.Exists(templateFilePath))
                {
                    _ = _loggingService.LogDebugAsync($"Template file not found: {templateFilePath}");
                    return;
                }

                // Load image with BitmapCacheOption.OnLoad to avoid file locking
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(templateFilePath, UriKind.Absolute);
                bitmap.DecodePixelHeight = 100; // Thumbnail height
                bitmap.EndInit();
                bitmap.Freeze(); // Make it thread-safe

                TemplateImageSource = bitmap;
                OnPropertyChanged(nameof(HasTemplateImage));

                _ = _loggingService.LogDebugAsync($"Loaded template thumbnail: {templateFileName}");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error loading template image", ex);
                TemplateImageSource = null;
                OnPropertyChanged(nameof(HasTemplateImage));
            }
        }

        /// <summary>
        /// Shows the template image in a large popup window
        /// </summary>
        private async Task ShowLargeTemplateImageAsync()
        {
            if (SelectedStep == null || string.IsNullOrWhiteSpace(SelectedStep.TemplatePath))
                return;

            try
            {
                // Construct template file path
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = System.IO.Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFileName = SelectedStep.TemplatePath.EndsWith(".png")
                    ? SelectedStep.TemplatePath
                    : $"{SelectedStep.TemplatePath}.png";
                var templateFilePath = System.IO.Path.Combine(templatesPath, templateFileName);

                if (!System.IO.File.Exists(templateFilePath))
                {
                    await _dialogService.ShowMessageDialogAsync("Template Not Found",
                        $"Template file not found:\n{templateFilePath}");
                    return;
                }

                // Create a simple window to display the image
                var window = new System.Windows.Window
                {
                    Title = $"Template Preview: {SelectedStep.DisplayName}",
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    Owner = System.Windows.Application.Current?.MainWindow,
                    Content = new System.Windows.Controls.Image
                    {
                        Source = new BitmapImage(new Uri(templateFilePath, UriKind.Absolute)),
                        Stretch = System.Windows.Media.Stretch.Uniform
                    }
                };

                window.ShowDialog();

                await _loggingService.LogDebugAsync($"Showed large template image: {templateFileName}");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error showing large template image", ex);
                await _dialogService.ShowMessageDialogAsync("Display Error",
                    $"Failed to display template image: {ex.Message}");
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Unsubscribe from all event handlers to prevent memory leaks
            UnsubscribeFromWorkflowChanges();
            UnsubscribeFromStepChanges();

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}

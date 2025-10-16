using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
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

        private ObservableCollection<WorkflowDefinition> _workflows;
        private WorkflowDefinition? _selectedWorkflow;
        private WorkflowStepDefinition? _selectedStep;
        private KeyboardAction? _selectedNavigationAction;
        private bool _isLoading;
        private bool _hasUnsavedChanges;
        private bool _disposed;
        private CancellationTokenSource _cancellationTokenSource = new();

        public WorkflowEditorViewModel(
            IWorkflowService workflowService,
            ILoggingService loggingService,
            IDialogService dialogService,
            IUiDispatcher uiDispatcher,
            WorkflowTaskBuilder taskBuilder,
            ITemplateManagementService templateService,
            IScreenshotCaptureService screenshotService,
            IServiceProvider serviceProvider)
        {
            _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            _taskBuilder = taskBuilder ?? throw new ArgumentNullException(nameof(taskBuilder));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            _workflows = new ObservableCollection<WorkflowDefinition>();

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
                    SelectedStep = null;
                    OnPropertyChanged(nameof(HasWorkflowSelected));
                    OnPropertyChanged(nameof(CanEditWorkflow));
                    OnPropertyChanged(nameof(WorkflowSteps));
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
                    OnPropertyChanged(nameof(HasStepSelected));
                    OnPropertyChanged(nameof(CanEditStep));
                    OnPropertyChanged(nameof(NavigationActions));
                    OnPropertyChanged(nameof(HasNavigationAction));
                    SelectedNavigationAction = null;
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
        public ICommand ViewTemplateImageCommand { get; private set; } = null!;

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

            ViewTemplateImageCommand = new RelayCommand(
                async () => await ViewTemplateImageAsync(),
                () => HasStepSelected && !string.IsNullOrEmpty(SelectedStep?.TemplatePath));
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

                SelectedWorkflow.LastModifiedDate = DateTime.UtcNow;
                var success = await _workflowService.SaveWorkflowAsync(SelectedWorkflow, _cancellationTokenSource.Token);

                if (success)
                {
                    HasUnsavedChanges = false;
                    await _loggingService.LogInfoAsync($"Saved workflow: {SelectedWorkflow.Name}");
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

                // Generate template path based on step ID (guaranteed unique)
                // Format: Application/stepid where stepid is the unique GUID
                var templateName = SelectedStep.StepId.ToLowerInvariant().Replace("-", "_");
                var templatePath = $"PlayOnline/{templateName}"; // Default to PlayOnline category

                await _loggingService.LogInfoAsync($"Creating template: {templatePath} for step '{SelectedStep.DisplayName}'");

                // Save cropped template image
                var success = await _templateService.CropAndReplaceTemplateImageAsync(
                    templatePath,
                    dialog.FileName,
                    cropViewModel.CropRectangle);

                if (success)
                {
                    // Update step with new template path
                    SelectedStep.TemplatePath = templatePath;
                    HasUnsavedChanges = true;
                    UpdateCommandStates();

                    await _loggingService.LogInfoAsync($"Template created successfully: {templatePath}");
                    await _dialogService.ShowMessageDialogAsync("Template Created",
                        $"Template '{templateName}' created successfully\n\nPath: {templatePath}");
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

        /// <summary>
        /// Opens a dialog to view the current template image with click point visualization
        /// </summary>
        private async Task ViewTemplateImageAsync()
        {
            if (SelectedStep == null || string.IsNullOrEmpty(SelectedStep.TemplatePath))
                return;

            try
            {
                // Get the dialog and ViewModel from DI
                var viewModel = _serviceProvider.GetService(typeof(TemplateViewerDialogViewModel)) as TemplateViewerDialogViewModel;
                var dialog = _serviceProvider.GetService(typeof(FFXIManager.Views.TemplateViewerDialog)) as System.Windows.Window;

                if (viewModel == null || dialog == null)
                {
                    await _dialogService.ShowMessageDialogAsync("Dialog Error",
                        "Failed to create template viewer dialog");
                    return;
                }

                // Load the template with click markers based on navigation sequence
                await viewModel.LoadTemplateAsync(SelectedStep.TemplatePath, SelectedStep.Navigation?.Sequence);

                // Show the dialog
                dialog.Owner = System.Windows.Application.Current?.MainWindow;
                dialog.ShowDialog();

                await _loggingService.LogDebugAsync($"Template viewer closed for: {SelectedStep.TemplatePath}");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error viewing template image", ex);
                await _dialogService.ShowMessageDialogAsync("View Failed",
                    $"Failed to view template image: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private async Task LoadDataAsync()
        {
            await LoadWorkflowsAsync();
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
            (ViewTemplateImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}

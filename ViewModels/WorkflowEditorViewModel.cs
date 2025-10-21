using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.ViewModels.Base;
using FFXIManager.ViewModels.WorkflowEditor;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media.Imaging;

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

        // SOLID Refactoring: Helper services for specialized operations
        private readonly WorkflowEditorNavigationManager _navigationManager;
        private readonly WorkflowEditorStepManager _stepManager;
        private readonly WorkflowEditorTemplateManager _templateManager;
        private readonly WorkflowEditorWorkflowManager _workflowManager;

        private ObservableCollection<WorkflowDefinition> _workflows;
        private ObservableCollection<ExternalApplication> _availableApplications;
        private WorkflowDefinition? _selectedWorkflow;
        private WorkflowStepDefinition? _selectedStep;
        private KeyboardAction? _selectedNavigationAction;
        private BitmapImage? _templateImageSource;
        private BitmapImage? _actionTemplateImageSource;
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
            IExternalApplicationService externalApplicationService,
            WorkflowEditorNavigationManager navigationManager,
            WorkflowEditorStepManager stepManager,
            WorkflowEditorTemplateManager templateManager,
            WorkflowEditorWorkflowManager workflowManager)
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

            _navigationManager = navigationManager ?? throw new ArgumentNullException(nameof(navigationManager));
            _stepManager = stepManager ?? throw new ArgumentNullException(nameof(stepManager));
            _templateManager = templateManager ?? throw new ArgumentNullException(nameof(templateManager));
            _workflowManager = workflowManager ?? throw new ArgumentNullException(nameof(workflowManager));

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

                    // Auto-select first step of the selected workflow for a smoother UX
                    SelectedStep = _selectedWorkflow?.Steps?.FirstOrDefault();
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

                    // Notify step-level retry configuration properties
                    OnPropertyChanged(nameof(StepEstimatedDurationSeconds));
                    OnPropertyChanged(nameof(StepRetryAttempts));
                    OnPropertyChanged(nameof(StepRetryDelayMs));
                    OnPropertyChanged(nameof(StepRetryBudgetInfo));

                    SelectedNavigationAction = null;

                    // Load template image preview
                    LoadTemplateImage();

                    // Subscribe to new step
                    SubscribeToStepChanges();

                    // Auto-select the first action in the sequence (more intuitive UX)
                    if (SelectedStep?.Navigation?.Sequence != null)
                    {
                        SelectedNavigationAction = SelectedStep.Navigation.Sequence.FirstOrDefault();
                    }

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
                    OnPropertyChanged(nameof(IsClickActionSelected));
                    OnPropertyChanged(nameof(IsKeyboardActionSelected));
                    OnPropertyChanged(nameof(IsWaitActionSelected));
                    OnPropertyChanged(nameof(IsInputPasswordActionSelected));
                    OnPropertyChanged(nameof(IsInputOTPActionSelected));
                    OnPropertyChanged(nameof(IsMemberSlotActionSelected));
                    OnPropertyChanged(nameof(IsCharacterSlotActionSelected));
                    OnPropertyChanged(nameof(IsSlotActionSelected));
                    OnPropertyChanged(nameof(SelectedLaunchApplication));
                    OnPropertyChanged(nameof(LaunchApplicationId));
                    OnPropertyChanged(nameof(LaunchApplicationName));
                    OnPropertyChanged(nameof(LaunchAllowSkipIfRunning));
                    OnPropertyChanged(nameof(LaunchAllowSkipIfNotConfigured));
                    // NavigationMethod removed for MemberSlot MVP
                    OnPropertyChanged(nameof(ActionTemplatePath));
                    OnPropertyChanged(nameof(ActionConfidenceThreshold));
                    OnPropertyChanged(nameof(ActionTolerance));
                    OnPropertyChanged(nameof(ActionRetryAttempts));
                    OnPropertyChanged(nameof(ActionRetryDelayMs));
                    OnPropertyChanged(nameof(ActionTimeoutSeconds));
                    OnPropertyChanged(nameof(ActionRequireMatch));
                    OnPropertyChanged(nameof(ActionTargetApplication));
                    OnPropertyChanged(nameof(SelectedActionClickPoints));
                    LoadActionTemplateImage();
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
        /// Whether the selected navigation action is a Click action
        /// </summary>
        public bool IsClickActionSelected => SelectedNavigationAction?.Action == "Click";

        /// <summary>
        /// Whether the selected navigation action is a Keyboard action (or a direct key like Tab/Enter)
        /// </summary>
        public bool IsKeyboardActionSelected => SelectedNavigationAction?.Action == "Keyboard"
            || IsKeyboardKey(SelectedNavigationAction?.Action);

        // Removed NavigationMethod and per-action slot ClickX/ClickY for MVP

        // (Removed legacy Home/reset options for DX9)

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
        /// Action-level template image source for thumbnail preview
        /// </summary>
        public BitmapImage? ActionTemplateImageSource
        {
            get => _actionTemplateImageSource;
            set
            {
                if (SetProperty(ref _actionTemplateImageSource, value))
                {
                    OnPropertyChanged(nameof(HasActionTemplateImage));
                }
            }
        }

        /// <summary>
        /// Whether the selected action has a template image
        /// </summary>
        public bool HasActionTemplateImage => ActionTemplateImageSource != null;

        public bool HasAnyTemplateForAction =>
            !string.IsNullOrEmpty(ActionTemplatePath) || !string.IsNullOrWhiteSpace(SelectedStep?.TemplatePath);

        /// <summary>
        /// Exposes ClickPoints for the selected action for thumbnail overlays.
        /// </summary>
        public System.Collections.Generic.IList<RelativeClickOffset> SelectedActionClickPoints
        {
            get
            {
                if (SelectedNavigationAction == null) return new System.Collections.Generic.List<RelativeClickOffset>();
                var list = SelectedNavigationAction.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new List<RelativeClickOffset>());
                return list ?? new System.Collections.Generic.List<RelativeClickOffset>();
            }
        }

        #region Step-Level Retry Configuration Properties

        /// <summary>
        /// Estimated duration for the step in seconds.
        /// Automatically validates against retry budget and adjusts if needed.
        /// Minimum value: 1 second
        /// </summary>
        public int StepEstimatedDurationSeconds
        {
            get => SelectedStep?.EstimatedDurationSeconds ?? 5;
            set
            {
                if (SelectedStep != null)
                {
                    // Clamp to minimum of 1 second
                    var validatedValue = Math.Max(1, value);

                    if (SelectedStep.EstimatedDurationSeconds != validatedValue)
                    {
                        SelectedStep.EstimatedDurationSeconds = validatedValue;
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(StepRetryBudgetInfo));
                        ValidateRetryBudget();
                        HasUnsavedChanges = true;
                    }
                }
            }
        }

        /// <summary>
        /// Number of polling attempts for template detection.
        /// Triggers validation when changed to ensure EstimatedDurationSeconds is sufficient.
        /// Minimum value: 1 (if specified)
        /// </summary>
        public int? StepRetryAttempts
        {
            get => SelectedStep?.RetryAttempts;
            set
            {
                if (SelectedStep != null)
                {
                    // Validate: must be positive if not null
                    int? validatedValue = value.HasValue ? Math.Max(1, value.Value) : null;

                    if (SelectedStep.RetryAttempts != validatedValue)
                    {
                        SelectedStep.RetryAttempts = validatedValue;
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(StepRetryBudgetInfo));
                        ValidateRetryBudget();
                        HasUnsavedChanges = true;
                    }
                }
            }
        }

        /// <summary>
        /// Delay in milliseconds between template detection polling attempts.
        /// Triggers validation when changed to ensure EstimatedDurationSeconds is sufficient.
        /// Minimum value: 100ms (if specified)
        /// </summary>
        public int? StepRetryDelayMs
        {
            get => SelectedStep?.RetryDelayMs;
            set
            {
                if (SelectedStep != null)
                {
                    // Validate: must be at least 100ms if not null (matches service layer minimum)
                    int? validatedValue = value.HasValue ? Math.Max(100, value.Value) : null;

                    if (SelectedStep.RetryDelayMs != validatedValue)
                    {
                        SelectedStep.RetryDelayMs = validatedValue;
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(StepRetryBudgetInfo));
                        ValidateRetryBudget();
                        HasUnsavedChanges = true;
                    }
                }
            }
        }

        /// <summary>
        /// Information about retry time budget for user feedback
        /// </summary>
        public string StepRetryBudgetInfo
        {
            get
            {
                if (SelectedStep == null) return string.Empty;

                var attempts = SelectedStep.RetryAttempts ?? 30;
                var delayMs = SelectedStep.RetryDelayMs ?? 500;
                var totalSeconds = (attempts * delayMs) / 1000.0;
                var estimated = SelectedStep.EstimatedDurationSeconds;

                if (totalSeconds > estimated)
                {
                    return $"⚠ Retry budget ({totalSeconds:F1}s) exceeds timeout ({estimated}s)";
                }
                else
                {
                    return $"✓ Retry budget: {totalSeconds:F1}s of {estimated}s";
                }
            }
        }

        /// <summary>
        /// Validates that EstimatedDurationSeconds is at least as large as the retry budget.
        /// Auto-adjusts EstimatedDurationSeconds if needed.
        /// </summary>
        private void ValidateRetryBudget()
        {
            if (SelectedStep == null) return;

            var attempts = SelectedStep.RetryAttempts ?? 30;
            var delayMs = SelectedStep.RetryDelayMs ?? 500;
            var totalSeconds = (int)Math.Ceiling((attempts * delayMs) / 1000.0);

            if (totalSeconds > SelectedStep.EstimatedDurationSeconds)
            {
                // Auto-adjust EstimatedDurationSeconds to accommodate retry budget
                SelectedStep.EstimatedDurationSeconds = totalSeconds;
                OnPropertyChanged(nameof(StepEstimatedDurationSeconds));
                OnPropertyChanged(nameof(StepRetryBudgetInfo));

                _ = _loggingService.LogInfoAsync($"Auto-adjusted EstimatedDurationSeconds to {totalSeconds}s to accommodate retry budget");
            }
        }

        #endregion

        // Common action-level template bindings
        public string? ActionTemplatePath
        {
            get => SelectedNavigationAction?.GetParameter<string?>("TemplatePath", null);
            set
            {
                if (SelectedNavigationAction != null)
                {
                    SelectedNavigationAction.SetParameter("TemplatePath", value ?? string.Empty);
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                    // Invalidate cached variants for this template path (with/without extension)
                    try
                    {
                        var p = value ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(p))
                        {
                            _templateService.Invalidate(p);
                            if (!p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                _templateService.Invalidate(p + ".png");
                        }
                    }
                    catch { }
                    LoadActionTemplateImage();
                }
            }
        }

        public float ActionConfidenceThreshold
        {
            get => SelectedNavigationAction?.GetParameter<float>("ConfidenceThreshold", 0.8f) ?? 0.8f;
            set
            {
                if (SelectedNavigationAction != null)
                {
                    SelectedNavigationAction.SetParameter("ConfidenceThreshold", value);
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public int ActionTolerance
        {
            get => SelectedNavigationAction?.GetParameter<int>("Tolerance", 5) ?? 5;
            set
            {
                if (SelectedNavigationAction != null)
                {
                    SelectedNavigationAction.SetParameter("Tolerance", value);
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public int ActionRetryAttempts
        {
            get => SelectedNavigationAction?.GetParameter<int>("RetryAttempts", 30) ?? 30;
            set
            {
                if (SelectedNavigationAction != null)
                {
                    SelectedNavigationAction.SetParameter("RetryAttempts", value);
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public int ActionRetryDelayMs
        {
            get => SelectedNavigationAction?.GetParameter<int>("RetryDelayMs", 500) ?? 500;
            set
            {
                if (SelectedNavigationAction != null)
                {
                    SelectedNavigationAction.SetParameter("RetryDelayMs", value);
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public int ActionTimeoutSeconds
        {
            get => SelectedNavigationAction?.GetParameter<int>("TimeoutSeconds", 30) ?? 30;
            set
            {
                if (SelectedNavigationAction != null)
                {
                    SelectedNavigationAction.SetParameter("TimeoutSeconds", value);
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public bool ActionRequireMatch
        {
            get => SelectedNavigationAction?.GetParameter<bool>("RequireMatch", false) ?? false;
            set
            {
                if (SelectedNavigationAction != null)
                {
                    SelectedNavigationAction.SetParameter("RequireMatch", value);
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public string? ActionTargetApplication
        {
            get => SelectedNavigationAction?.GetParameter<string?>("TargetApplication", null);
            set
            {
                if (SelectedNavigationAction != null)
                {
                    SelectedNavigationAction.SetParameter("TargetApplication", value ?? string.Empty);
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

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
                    // If a matching application exists, also set ApplicationId for stability
                    var app = AvailableApplications.FirstOrDefault(a => a.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
                    if (app != null)
                    {
                        SelectedNavigationAction.SetParameter("ApplicationId", app.Id.ToString());
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LaunchApplicationId));
                    OnPropertyChanged(nameof(SelectedLaunchApplication));
                    HasUnsavedChanges = true;
                }
            }
        }

        /// <summary>
        /// Stable application identifier (GUID) for Launch actions
        /// </summary>
        public Guid? LaunchApplicationId
        {
            get
            {
                var idStr = SelectedNavigationAction?.GetParameter<string>("ApplicationId", string.Empty);
                if (Guid.TryParse(idStr, out var id)) return id;
                return null;
            }
            set
            {
                if (SelectedNavigationAction != null)
                {
                    var idText = value.HasValue ? value.Value.ToString() : string.Empty;
                    SelectedNavigationAction.SetParameter("ApplicationId", idText);

                    // Also maintain ApplicationName for better UI summaries
                    if (value.HasValue)
                    {
                        var app = AvailableApplications.FirstOrDefault(a => a.Id == value.Value);
                        if (app != null)
                        {
                            SelectedNavigationAction.SetParameter("ApplicationName", app.Name);
                        }
                    }

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LaunchApplicationName));
                    OnPropertyChanged(nameof(SelectedLaunchApplication));
                    HasUnsavedChanges = true;
                }
            }
        }

        /// <summary>
        /// Helper for binding a ComboBox to the selected application object.
        /// </summary>
        public ExternalApplication? SelectedLaunchApplication
        {
            get
            {
                var id = LaunchApplicationId;
                if (id.HasValue)
                {
                    return AvailableApplications.FirstOrDefault(a => a.Id == id.Value);
                }
                // fallback by name
                var name = LaunchApplicationName;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return AvailableApplications.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                }
                return null;
            }
            set
            {
                if (SelectedNavigationAction != null)
                {
                    if (value != null)
                    {
                        SelectedNavigationAction.SetParameter("ApplicationId", value.Id.ToString());
                        SelectedNavigationAction.SetParameter("ApplicationName", value.Name);
                    }
                    else
                    {
                        SelectedNavigationAction.SetParameter("ApplicationId", string.Empty);
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LaunchApplicationId));
                    OnPropertyChanged(nameof(LaunchApplicationName));
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
        public ICommand OpenWorkflowGuideCommand { get; private set; } = null!;

        // Template image management commands
        public ICommand SelectTemplateImageCommand { get; private set; } = null!;
        public ICommand ReplaceTemplateImageCommand { get; private set; } = null!;
        public ICommand ShowLargeTemplateImageCommand { get; private set; } = null!;
        public ICommand SelectActionTemplateImageCommand { get; private set; } = null!;
        public ICommand ReplaceActionTemplateImageCommand { get; private set; } = null!;
        public ICommand ShowLargeActionTemplateImageCommand { get; private set; } = null!;
        public ICommand PickMemberSlotClickPointsCommand { get; private set; } = null!;
        public ICommand PreviewMemberSlotClickPointsCommand { get; private set; } = null!;

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

            OpenWorkflowGuideCommand = new RelayCommand(
                async () => await OpenWorkflowGuideAsync());

            SelectTemplateImageCommand = new RelayCommand(
                async () => await SelectTemplateImageAsync(),
                () => CanEditStep);

            ReplaceTemplateImageCommand = new RelayCommand(
                async () => await ReplaceTemplateImageAsync(),
                () => CanEditStep && !string.IsNullOrEmpty(SelectedStep?.TemplatePath));

            ShowLargeTemplateImageCommand = new RelayCommand(
                async () => await ShowLargeTemplateImageAsync(),
                () => HasTemplateImage);

            SelectActionTemplateImageCommand = new RelayCommand(
                async () => await SelectActionTemplateImageAsync(),
                () => CanEditNavigationAction);

            ReplaceActionTemplateImageCommand = new RelayCommand(
                async () => await ReplaceActionTemplateImageAsync(),
                () => CanEditNavigationAction && !string.IsNullOrEmpty(ActionTemplatePath));

            ShowLargeActionTemplateImageCommand = new RelayCommand(
                async () => await ShowLargeActionTemplateImageAsync(),
                () => HasAnyTemplateForAction);

            PickMemberSlotClickPointsCommand = new RelayCommand(
                async () =>
                {
                    if (SelectedStep != null && SelectedNavigationAction != null)
                    {
                        var changed = await _templateManager.PickMemberSlotClickPositionsForActionAsync(SelectedStep, SelectedNavigationAction);
                        if (changed)
                        {
                            HasUnsavedChanges = true;
                            OnPropertyChanged(nameof(SelectedStep));
                        }
                    }
                },
                () => IsMemberSlotActionSelected && SelectedStep != null && !string.IsNullOrWhiteSpace(SelectedStep.TemplatePath));

            PreviewMemberSlotClickPointsCommand = new RelayCommand(
                async () =>
                {
                    if (SelectedStep != null && SelectedNavigationAction != null)
                    {
                        await _templateManager.ShowMemberSlotClickPositionsAsync(SelectedStep, SelectedNavigationAction);
                    }
                },
                () => IsMemberSlotActionSelected && SelectedStep != null && !string.IsNullOrWhiteSpace(SelectedStep.TemplatePath));

        }

        #endregion

        #region Command Implementations

        private async Task LoadWorkflowsAsync()
        {
            IsLoading = true;
            try
            {
                // Delegate to helper
                var workflows = await _workflowManager.LoadWorkflowsAsync(_cancellationTokenSource.Token);

                // Update UI state
                await _uiDispatcher.InvokeAsync(() =>
                {
                    Workflows.Clear();
                    foreach (var workflow in workflows.OrderBy(w => w.Name))
                    {
                        Workflows.Add(workflow);
                    }

                    // Auto-select default workflow (or first) if available
                    if (Workflows.Count > 0)
                    {
                        var defaultWf = Workflows.FirstOrDefault(w => w.IsDefault) ?? Workflows.FirstOrDefault();
                        if (defaultWf != null)
                        {
                            SelectedWorkflow = defaultWf;
                        }
                    }
                });
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
                // Delegate to helper
                var newWorkflow = await _workflowManager.CreateNewWorkflowAsync();

                // Update UI state
                await _uiDispatcher.InvokeAsync(() =>
                {
                    Workflows.Add(newWorkflow);
                    SelectedWorkflow = newWorkflow;
                });

                HasUnsavedChanges = true;
            }
            catch
            {
                // Error already handled and logged by helper
            }
        }

        private async Task SaveWorkflowAsync()
        {
            if (SelectedWorkflow == null) return;

            // Delegate to helper
            var (success, savedWorkflowId, savedWorkflowName) = await _workflowManager.SaveWorkflowAsync(
                SelectedWorkflow,
                _cancellationTokenSource.Token);

            if (success)
            {
                // Update UI state
                HasUnsavedChanges = false;

                // Reload workflows to reflect the saved workflow
                await LoadWorkflowsAsync();

                // Reselect the workflow by its ID (may have changed if converted from default)
                await _uiDispatcher.InvokeAsync(() =>
                {
                    var reloadedWorkflow = Workflows.FirstOrDefault(w => w != null && w.WorkflowId == savedWorkflowId);
                    if (reloadedWorkflow != null)
                    {
                        SelectedWorkflow = reloadedWorkflow;
                    }
                });
            }
        }

        private async Task DeleteWorkflowAsync()
        {
            if (SelectedWorkflow == null) return;

            // Delegate to helper
            var deleted = await _workflowManager.DeleteWorkflowAsync(SelectedWorkflow, _cancellationTokenSource.Token);

            if (deleted)
            {
                // Update UI state
                await _uiDispatcher.InvokeAsync(() =>
                {
                    Workflows.Remove(SelectedWorkflow);
                    SelectedWorkflow = null;
                });
            }
        }

        private async Task CloneWorkflowAsync()
        {
            if (SelectedWorkflow == null) return;

            // Delegate to helper
            var clonedWorkflow = await _workflowManager.CloneWorkflowAsync(
                SelectedWorkflow.WorkflowId,
                $"{SelectedWorkflow.Name} (Copy)",
                _cancellationTokenSource.Token);

            if (clonedWorkflow != null)
            {
                // Update UI state
                await _uiDispatcher.InvokeAsync(() =>
                {
                    Workflows.Add(clonedWorkflow);
                    SelectedWorkflow = clonedWorkflow;
                });

                HasUnsavedChanges = true;
            }
        }

        private async Task ImportWorkflowAsync()
        {
            // Delegate to helper
            await _workflowManager.ImportWorkflowAsync();
        }

        private async Task ExportWorkflowAsync()
        {
            if (SelectedWorkflow == null) return;

            // Delegate to helper
            await _workflowManager.ExportWorkflowAsync();
        }

        private async Task RestoreDefaultWorkflowsAsync()
        {
            // Delegate to helper
            var count = await _workflowManager.RestoreDefaultWorkflowsAsync(_cancellationTokenSource.Token);

            if (count > 0)
            {
                // Update UI state - reload workflows to reflect restored defaults
                await LoadWorkflowsAsync();
            }
        }

        private async Task AddStepAsync()
        {
            if (SelectedWorkflow == null) return;

            try
            {
                var newStep = await _stepManager.AddStepAsync(SelectedWorkflow);
                SelectedStep = newStep;
                HasUnsavedChanges = true;
                OnPropertyChanged(nameof(WorkflowSteps));
            }
            catch
            {
                // Error already handled and logged by helper
            }
        }

        private async Task RemoveStepAsync()
        {
            if (SelectedWorkflow == null || SelectedStep == null) return;

            var removed = await _stepManager.RemoveStepAsync(SelectedWorkflow, SelectedStep);

            if (removed)
            {
                SelectedStep = null;
                HasUnsavedChanges = true;
                OnPropertyChanged(nameof(WorkflowSteps));
            }
        }

        private void MoveStepUp()
        {
            if (SelectedWorkflow == null || SelectedStep == null) return;

            var moved = _stepManager.MoveStepUp(SelectedWorkflow, SelectedStep);

            if (moved)
            {
                HasUnsavedChanges = true;
                OnPropertyChanged(nameof(WorkflowSteps));
                UpdateCommandStates();
            }
        }

        private void MoveStepDown()
        {
            if (SelectedWorkflow == null || SelectedStep == null) return;

            var moved = _stepManager.MoveStepDown(SelectedWorkflow, SelectedStep);

            if (moved)
            {
                HasUnsavedChanges = true;
                OnPropertyChanged(nameof(WorkflowSteps));
                UpdateCommandStates();
            }
        }

        private async Task EditStepAsync()
        {
            if (SelectedStep == null) return;
            await _stepManager.EditStepAsync(SelectedStep);
        }

        private void InitializeNavigation()
        {
            if (SelectedStep == null) return;

            _navigationManager.InitializeNavigation(SelectedStep);

            // Update UI state
            OnPropertyChanged(nameof(NavigationActions));
            OnPropertyChanged(nameof(HasNavigationAction));
            HasUnsavedChanges = true;

            // Subscribe to navigation property changes now that it exists
            if (SelectedStep.Navigation is INotifyPropertyChanged navigationNotifier)
            {
                navigationNotifier.PropertyChanged += OnNavigationPropertyChanged;
                _subscribedNavigation = SelectedStep.Navigation;
            }

            // Subscribe to the newly created navigation sequence
            SubscribeToNavigationSequenceChanges();

            UpdateCommandStates();
        }

        private void AddNavigationAction()
        {
            if (SelectedStep?.Navigation == null) return;

            var newAction = _navigationManager.AddNavigationAction(SelectedStep);

            if (newAction != null)
            {
                SelectedNavigationAction = newAction;
                HasUnsavedChanges = true;
                OnPropertyChanged(nameof(NavigationActions));
                UpdateCommandStates();
            }
        }

        private void RemoveNavigationAction()
        {
            if (SelectedStep?.Navigation == null || SelectedNavigationAction == null) return;

            var removed = _navigationManager.RemoveNavigationAction(SelectedStep, SelectedNavigationAction);

            if (removed)
            {
                SelectedNavigationAction = null;
                HasUnsavedChanges = true;
                OnPropertyChanged(nameof(NavigationActions));
                UpdateCommandStates();
            }
        }

        private void MoveNavigationActionUp()
        {
            if (SelectedStep?.Navigation == null || SelectedNavigationAction == null) return;

            var moved = _navigationManager.MoveNavigationActionUp(SelectedStep, SelectedNavigationAction);

            if (moved)
            {
                HasUnsavedChanges = true;
                UpdateCommandStates();
            }
        }

        private void MoveNavigationActionDown()
        {
            if (SelectedStep?.Navigation == null || SelectedNavigationAction == null) return;

            var moved = _navigationManager.MoveNavigationActionDown(SelectedStep, SelectedNavigationAction);

            if (moved)
            {
                HasUnsavedChanges = true;
                UpdateCommandStates();
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

                // Open image cropper dialog
                var cropViewModel = new ImageCropperDialogViewModel(dialog.FileName, _loggingService);
                var cropDialog = new Views.ImageCropperDialog(cropViewModel)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };

                if (cropDialog.ShowDialog() != true)
                    return;

                // Validate crop rectangle
                if (cropViewModel.CropRectangle == null)
                {
                    await _dialogService.ShowMessageDialogAsync("Invalid Selection", "No crop area was selected.");
                    return;
                }

                // Delegate to helper
                var success = await _templateManager.CreateStepTemplateAsync(
                    SelectedStep,
                    dialog.FileName,
                    cropViewModel.CropRectangle.Value);

                if (success)
                {
                    // Update UI state
                    HasUnsavedChanges = true;
                    UpdateCommandStates();
                    LoadTemplateImage();
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
                // Open file dialog to select new image
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select New Image for Template",
                    Filter = "Image Files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|All Files (*.*)|*.*",
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true)
                    return;

                // Open image cropper dialog
                var cropViewModel = new ImageCropperDialogViewModel(dialog.FileName, _loggingService);
                var cropDialog = new Views.ImageCropperDialog(cropViewModel)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };

                if (cropDialog.ShowDialog() != true)
                    return;

                // Delegate to helper
                var success = await _templateManager.ReplaceStepTemplateAsync(
                    SelectedStep,
                    dialog.FileName,
                    cropViewModel.CropRectangle);

                if (success)
                {
                    // Update UI state + targeted invalidation
                    try
                    {
                        var p = SelectedStep.TemplatePath;
                        if (!string.IsNullOrWhiteSpace(p))
                        {
                            _templateService.Invalidate(p);
                            if (!p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                _templateService.Invalidate(p + ".png");
                        }
                    }
                    catch { }
                    LoadTemplateImage();
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
            (SelectActionTemplateImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ReplaceActionTemplateImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowLargeActionTemplateImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                OnPropertyChanged(nameof(IsClickActionSelected));
                OnPropertyChanged(nameof(IsKeyboardActionSelected));
                OnPropertyChanged(nameof(IsWaitActionSelected));
                OnPropertyChanged(nameof(IsInputPasswordActionSelected));
                OnPropertyChanged(nameof(IsInputOTPActionSelected));
                OnPropertyChanged(nameof(IsMemberSlotActionSelected));
                OnPropertyChanged(nameof(IsCharacterSlotActionSelected));
                OnPropertyChanged(nameof(IsSlotActionSelected));
                OnPropertyChanged(nameof(LaunchApplicationName));
                OnPropertyChanged(nameof(LaunchAllowSkipIfRunning));
                OnPropertyChanged(nameof(LaunchAllowSkipIfNotConfigured));
                // NavigationMethod removed for MemberSlot MVP
                OnPropertyChanged(nameof(ActionTemplatePath));
                OnPropertyChanged(nameof(ActionConfidenceThreshold));
                OnPropertyChanged(nameof(ActionTolerance));
                OnPropertyChanged(nameof(ActionRetryAttempts));
                OnPropertyChanged(nameof(ActionRetryDelayMs));
                OnPropertyChanged(nameof(ActionTimeoutSeconds));
                OnPropertyChanged(nameof(ActionRequireMatch));
                OnPropertyChanged(nameof(ActionTargetApplication));
                OnPropertyChanged(nameof(SelectedActionClickPoints));
                LoadActionTemplateImage();
            }

            // Parameters changed may update template path, reload preview
            if (e.PropertyName == nameof(KeyboardAction.Parameters) && sender == SelectedNavigationAction)
            {
                LoadActionTemplateImage();
                OnPropertyChanged(nameof(SelectedActionClickPoints));
            }
        }

        private async Task OpenWorkflowGuideAsync()
        {
            try
            {
                string? readmePath = null;

                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var appReadme = System.IO.Path.Combine(appDir, "workflows", "README.md");
                if (System.IO.File.Exists(appReadme))
                {
                    readmePath = appReadme;
                }
                else
                {
                    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var appDataReadme = System.IO.Path.Combine(appDataPath, "FFXIManager", "workflows", "README.md");
                    if (System.IO.File.Exists(appDataReadme))
                    {
                        readmePath = appDataReadme;
                    }
                }

                if (string.IsNullOrEmpty(readmePath))
                {
                    await _dialogService.ShowMessageDialogAsync("Guide Not Found", "Could not locate workflows/README.md.");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = readmePath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Failed to open workflow guide", ex);
                await _dialogService.ShowMessageDialogAsync("Open Failed", $"Failed to open workflow guide: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the template image for the currently selected step
        /// </summary>
        private void LoadTemplateImage()
        {
            try
            {
                // Delegate to helper
                TemplateImageSource = _templateManager.LoadStepTemplateThumbnail(SelectedStep);
                OnPropertyChanged(nameof(HasTemplateImage));
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error loading template image", ex);
                TemplateImageSource = null;
                OnPropertyChanged(nameof(HasTemplateImage));
            }
        }

        /// <summary>
        /// Loads the action-level template image for the selected action
        /// </summary>
        private void LoadActionTemplateImage()
        {
            try
            {
                // Delegate to helper
                ActionTemplateImageSource = _templateManager.LoadActionTemplateThumbnail(SelectedNavigationAction);
                OnPropertyChanged(nameof(ActionTemplatePath));
                OnPropertyChanged(nameof(HasActionTemplateImage));
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error loading action template image", ex);
                ActionTemplateImageSource = null;
                OnPropertyChanged(nameof(ActionTemplatePath));
                OnPropertyChanged(nameof(HasActionTemplateImage));
            }
        }

        private async Task ShowLargeActionTemplateImageAsync()
        {
            if (SelectedNavigationAction == null)
                return;

            if (string.Equals(SelectedNavigationAction.Action, "Click", StringComparison.OrdinalIgnoreCase))
            {
                var changed = await _templateManager.PickClickPositionForActionAsync(SelectedStep, SelectedNavigationAction);
                if (changed)
                {
                    HasUnsavedChanges = true;
                    OnPropertyChanged(nameof(SelectedNavigationAction));
                }
            }
            else if (IsMemberSlotActionSelected)
            {
                if (SelectedStep != null && SelectedNavigationAction != null)
                {
                    var changed = await _templateManager.PickMemberSlotClickPositionsForActionAsync(SelectedStep, SelectedNavigationAction);
                    if (changed)
                    {
                        HasUnsavedChanges = true;
                        OnPropertyChanged(nameof(SelectedStep));
                    }
                }
            }
            else
            {
                await _templateManager.ShowLargeActionTemplateAsync(SelectedNavigationAction);
            }
        }



        private async Task SelectActionTemplateImageAsync()
        {
            if (SelectedNavigationAction == null) return;

            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Screenshot or Image for Action Template",
                    Filter = "Image Files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|All Files (*.*)|*.*",
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true)
                    return;

                var cropViewModel = new ImageCropperDialogViewModel(dialog.FileName, _loggingService);
                var cropDialog = new Views.ImageCropperDialog(cropViewModel)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };

                if (cropDialog.ShowDialog() != true)
                    return;

                if (cropViewModel.CropRectangle == null)
                {
                    await _dialogService.ShowMessageDialogAsync("Invalid Selection", "No crop area was selected.");
                    return;
                }

                // Delegate to helper
                var success = await _templateManager.CreateActionTemplateAsync(
                    SelectedNavigationAction,
                    SelectedStep,
                    dialog.FileName,
                    cropViewModel.CropRectangle.Value);

                if (success)
                {
                    // Update UI state
                    LoadActionTemplateImage();
                    HasUnsavedChanges = true;
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error selecting action template image", ex);
            }
        }

        private async Task ReplaceActionTemplateImageAsync()
        {
            if (SelectedNavigationAction == null)
                return;

            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select New Image for Action Template",
                    Filter = "Image Files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|All Files (*.*)|*.*",
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true) return;

                var cropViewModel = new ImageCropperDialogViewModel(dialog.FileName, _loggingService);
                var cropDialog = new Views.ImageCropperDialog(cropViewModel)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };

                if (cropDialog.ShowDialog() != true) return;

                // Delegate to helper
                var success = await _templateManager.ReplaceActionTemplateAsync(
                    SelectedNavigationAction,
                    dialog.FileName,
                    cropViewModel.CropRectangle);

                if (success)
                {
                    // Update UI state + targeted invalidation
                    try
                    {
                        var p = SelectedNavigationAction.GetParameter<string>("TemplatePath", string.Empty);
                        if (!string.IsNullOrWhiteSpace(p))
                        {
                            _templateService.Invalidate(p);
                            if (!p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                _templateService.Invalidate(p + ".png");
                        }
                    }
                    catch { }
                    LoadActionTemplateImage();
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error replacing action template image", ex);
            }
        }

        private string GenerateActionTemplateName()
        {
            // Delegate to helper
            return _templateManager.GenerateActionTemplateName(SelectedStep, SelectedNavigationAction);
        }

        private static bool IsKeyboardKey(string? actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName)) return false;
            var a = actionName.ToLowerInvariant();
            switch (a)
            {
                case "tab":
                case "enter":
                case "return":
                case "escape":
                case "esc":
                case "space":
                case "spacebar":
                case "down":
                case "downarrow":
                case "up":
                case "uparrow":
                case "left":
                case "leftarrow":
                case "right":
                case "rightarrow":
                case "home":
                case "end":
                case "pageup":
                case "pagedown":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Shows the template image in a large popup window
        /// </summary>
        private async Task ShowLargeTemplateImageAsync()
        {
            if (SelectedStep == null || string.IsNullOrWhiteSpace(SelectedStep.TemplatePath))
                return;

            // If MemberSlot action is selected, use the 4-point picker directly from step preview
            if (IsMemberSlotActionSelected && SelectedNavigationAction != null)
            {
                var changed = await _templateManager.PickMemberSlotClickPositionsForActionAsync(SelectedStep, SelectedNavigationAction);
                if (changed)
                {
                    HasUnsavedChanges = true;
                    OnPropertyChanged(nameof(SelectedStep));
                }
            }
            else
            {
                // Delegate to helper
                await _templateManager.ShowLargeStepTemplateAsync(SelectedStep);
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

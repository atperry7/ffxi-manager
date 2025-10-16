using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.Services.AutoLogin.Configuration;
using FFXIManager.Infrastructure;

namespace FFXIManager.ViewModels
{
    public class TemplateNavigationTunerViewModel : INotifyPropertyChanged
    {
        private readonly ITemplateManagementService _templateService;
        private readonly ITemplateMatchingService _matchingService;
        private readonly IUIAutomationService _automation;
        private readonly IScreenshotCaptureService _screenshots;
        private readonly ILoggingService _log;
        private readonly IProcessManagementService _processes;
        private readonly IServiceProvider _serviceProvider;

        public ObservableCollection<string> Templates { get; } = new();

        private string? _selectedTemplate;
        public string? SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                _selectedTemplate = value;
                OnPropertyChanged();
                ((RelayCommand)ReplaceImageCommand).RaiseCanExecuteChanged();
                _ = LoadMetadataAsync();
            }
        }

        private TemplateMetadata? _metadata;
        public TemplateMetadata? Metadata
        {
            get => _metadata;
            set { _metadata = value; OnPropertyChanged(); }
        }

        private NavigationAction _currentNavigation = new NavigationAction();
        public NavigationAction CurrentNavigation
        {
            get => _currentNavigation;
            set
            {
                UnsubscribeFromNavigationChanges();
                _currentNavigation = value;
                SubscribeToNavigationChanges();
                OnPropertyChanged();
            }
        }

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                if (_hasUnsavedChanges != value)
                {
                    _hasUnsavedChanges = value;
                    OnPropertyChanged();
                    ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private string _status = string.Empty;
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        // Image display properties
        private BitmapSource? _templateImage;
        public BitmapSource? TemplateImage
        {
            get => _templateImage;
            set { _templateImage = value; OnPropertyChanged(); }
        }

        private double _templateImageWidth;
        public double TemplateImageWidth
        {
            get => _templateImageWidth;
            set { _templateImageWidth = value; OnPropertyChanged(); }
        }

        private double _templateImageHeight;
        public double TemplateImageHeight
        {
            get => _templateImageHeight;
            set { _templateImageHeight = value; OnPropertyChanged(); }
        }

        // Click markers for visualization
        public ObservableCollection<ClickMarker> ClickMarkers { get; } = new();

        // Click point editing mode
        private bool _isClickEditMode;
        public bool IsClickEditMode
        {
            get => _isClickEditMode;
            set
            {
                _isClickEditMode = value;
                OnPropertyChanged();
                UpdateStatus();
            }
        }

        private int _currentEditingClickIndex = 0;

        public ICommand RefreshCommand { get; }
        public ICommand TestCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand AddStepCommand { get; }
        public ICommand RemoveStepCommand { get; }
        public ICommand ReplaceImageCommand { get; }
        public ICommand OpenWorkflowEditorCommand { get; }

        // Window selection for live testing
        public class WindowEntry
        {
            public IntPtr Handle { get; set; }
            public int ProcessId { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Display => $"{ProcessName} — {Title} (PID {ProcessId})";
        }

        public ObservableCollection<WindowEntry> AvailableWindows { get; } = new();
        private WindowEntry? _selectedWindow;
        public WindowEntry? SelectedWindow
        {
            get => _selectedWindow;
            set { _selectedWindow = value; OnPropertyChanged(); }
        }

        public ICommand RefreshWindowsCommand { get; }

        /// <summary>
        /// Supported navigation actions: keyboard keys and mouse clicks
        /// Keyboard actions are based on KeyboardNavigationStrategy.ParseConsoleKey
        /// Click action uses relative coordinates (0.0-1.0) from template match location
        /// </summary>
        public static readonly string[] SupportedActions = new[]
        {
            "Click",
            "Tab",
            "Enter",
            "DownArrow",
            "UpArrow",
            "LeftArrow",
            "RightArrow",
            "Escape",
            "Spacebar",
            "Home",
            "End",
            "PageUp",
            "PageDown"
        };

        public enum TargetApp { PlayOnline, Windower, FFXI }
        private TargetApp _selectedTarget = TargetApp.PlayOnline;
        public TargetApp SelectedTarget { get => _selectedTarget; set { _selectedTarget = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;

        public TemplateNavigationTunerViewModel(
            ITemplateManagementService templateService,
            ITemplateMatchingService matchingService,
            IUIAutomationService automation,
            IScreenshotCaptureService screenshots,
            ILoggingService log,
            IProcessManagementService processes,
            IServiceProvider serviceProvider)
        {
            _templateService = templateService;
            _matchingService = matchingService;
            _automation = automation;
            _screenshots = screenshots;
            _log = log;
            _processes = processes;
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            RefreshCommand = new RelayCommand(async () => await LoadTemplatesAsync());
            TestCommand = new RelayCommand(async () => await TestNavigationAsync(), () => CanTest());
            SaveCommand = new RelayCommand(async () => await SaveAsync(), () => CanSave());
            AddStepCommand = new RelayCommand(() => AddStep());
            RemoveStepCommand = new RelayCommandWithParameter<KeyboardAction>(ka => RemoveStep(ka));
            RefreshWindowsCommand = new RelayCommand(async () => await RefreshWindowsAsync());
            ReplaceImageCommand = new RelayCommand(async () => await ReplaceTemplateImageAsync(), () => !string.IsNullOrEmpty(SelectedTemplate));
            OpenWorkflowEditorCommand = new RelayCommand(() => OpenWorkflowEditor());

            _ = LoadTemplatesAsync();
        }

        private async Task LoadTemplatesAsync()
        {
            Templates.Clear();
            foreach (var path in await _templateService.GetAvailableTemplatePathsAsync())
            {
                Templates.Add(path);
            }
            Status = $"Loaded {Templates.Count} templates";
        }

        private async Task LoadMetadataAsync()
        {
            if (string.IsNullOrEmpty(SelectedTemplate)) return;
            Metadata = await _templateService.GetTemplateMetadataAsync(SelectedTemplate!);

            // Auto-select target app based on template path prefix
            try
            {
                if (!string.IsNullOrEmpty(SelectedTemplate))
                {
                    var lower = SelectedTemplate!.ToLowerInvariant();
                    if (lower.StartsWith("playonline/")) SelectedTarget = TargetApp.PlayOnline;
                    else if (lower.StartsWith("windower/")) SelectedTarget = TargetApp.Windower;
                    else if (lower.StartsWith("ffxi/")) SelectedTarget = TargetApp.FFXI;
                }
            }
            catch { /* best-effort; ignore */ }
            if (Metadata?.Navigation == null)
            {
                CurrentNavigation = new NavigationAction { Type = NavigationType.Hybrid };
            }
            else
            {
                // Create a shallow copy so edits don't mutate original until saved
                // Always use Hybrid strategy - executes sequence of keyboard and click actions
                CurrentNavigation = new NavigationAction
                {
                    Type = NavigationType.Hybrid,
                    PostNavigationDelayMs = Metadata.Navigation.PostNavigationDelayMs,
                    Description = Metadata.Navigation.Description,
                    Sequence = new ObservableCollection<KeyboardAction>(Metadata.Navigation.Sequence)
                };
            }

            // Load template image and update click markers
            await LoadTemplateImageAsync();
            UpdateClickMarkers();

            await RefreshWindowsAsync();
        }

        private bool CanTest() => !string.IsNullOrEmpty(SelectedTemplate) && CurrentNavigation != null && SelectedWindow != null;
        private bool CanSave() => !string.IsNullOrEmpty(SelectedTemplate) && CurrentNavigation != null && HasUnsavedChanges;

        private async Task TestNavigationAsync()
        {
            if (string.IsNullOrEmpty(SelectedTemplate) || Metadata == null) return;

            try
            {
                if (SelectedWindow?.Handle == IntPtr.Zero || SelectedWindow == null)
                {
                    Status = "No target window found (start the app first)";
                    return;
                }

                // Capture window and detect the selected template
                var hwnd = SelectedWindow.Handle;
                var screenshot = await _screenshots.CaptureWindowAsync(hwnd, CancellationToken.None);
                if (screenshot == null)
                {
                    Status = "Failed to capture target window";
                    return;
                }

                var match = await _matchingService.FindElementAsync(screenshot, SelectedTemplate!, CancellationToken.None);
                if (match == null || match.Confidence < Metadata.ConfidenceThreshold)
                {
                    Status = $"Template not detected (conf {match?.Confidence:P})";
                    return;
                }

                await _automation.EnsureWindowFocusAsync(hwnd, CancellationToken.None);

                // Always use Hybrid strategy: tries keyboard first, falls back to click if available
                var strategy = new Services.AutoLogin.Navigation.HybridNavigationStrategy(_automation, _screenshots, _log);

                var ok = await strategy.ExecuteAsync(hwnd, CurrentNavigation, match, CancellationToken.None);
                Status = ok ? "Navigation executed successfully" : "Navigation execution failed";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                await _log.LogErrorAsync("Navigation test (dry-run) failed", ex);
            }
        }

        private async Task RefreshWindowsAsync()
        {
            AvailableWindows.Clear();
            string[] names = SelectedTarget switch
            {
                TargetApp.Windower => WindowerLaunchConfiguration.ProcessNames.WindowerVariations,
                TargetApp.PlayOnline => new[] { PlayOnlineAuthConfiguration.ProcessNames.PlayOnline },
                TargetApp.FFXI => new[] { PlayOnlineAuthConfiguration.ProcessNames.FFXIMain },
                _ => Array.Empty<string>()
            };

            var procs = await _processes.GetProcessesByNamesAsync(names);
            foreach (var p in procs)
            {
                var windows = await _processes.GetProcessWindowsAsync(p.ProcessId);
                foreach (var w in windows)
                {
                    AvailableWindows.Add(new WindowEntry
                    {
                        Handle = w.Handle,
                        ProcessId = p.ProcessId,
                        ProcessName = p.ProcessName,
                        Title = string.IsNullOrWhiteSpace(w.Title) ? p.MainWindowTitle : w.Title
                    });
                }
            }

            Status = AvailableWindows.Count > 0 ? $"Found {AvailableWindows.Count} windows" : "No windows found; open the app then Refresh";
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrEmpty(SelectedTemplate)) return;
            var ok = await _templateService.UpdateTemplateNavigationAsync(SelectedTemplate!, CurrentNavigation);
            Status = ok ? "Navigation saved" : "Save failed";
            if (ok)
            {
                HasUnsavedChanges = false;
                await LoadMetadataAsync();
            }
        }

        private void AddStep()
        {
            CurrentNavigation.Sequence.Add(new KeyboardAction { Action = "Tab", Count = 1, DelayMs = 100, Description = "New step" });
            HasUnsavedChanges = true;
        }

        private void RemoveStep(KeyboardAction? action)
        {
            if (action == null) return;
            CurrentNavigation.Sequence.Remove(action);
            HasUnsavedChanges = true;
        }

        private void SubscribeToNavigationChanges()
        {
            if (CurrentNavigation == null) return;

            // Subscribe to top-level navigation property changes (Type, PostNavigationDelayMs, Description)
            CurrentNavigation.PropertyChanged += OnNavigationPropertyChanged;

            // Subscribe to collection changes (add/remove items)
            CurrentNavigation.Sequence.CollectionChanged += OnSequenceCollectionChanged;

            // Subscribe to property changes on each existing item
            foreach (var item in CurrentNavigation.Sequence)
            {
                item.PropertyChanged += OnSequenceItemPropertyChanged;
            }
        }

        private void UnsubscribeFromNavigationChanges()
        {
            if (CurrentNavigation == null) return;

            CurrentNavigation.PropertyChanged -= OnNavigationPropertyChanged;
            CurrentNavigation.Sequence.CollectionChanged -= OnSequenceCollectionChanged;

            foreach (var item in CurrentNavigation.Sequence)
            {
                item.PropertyChanged -= OnSequenceItemPropertyChanged;
            }
        }

        private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Track changes to PostNavigationDelayMs and Description
            // Type is always Hybrid and not user-editable
            if (e.PropertyName == nameof(NavigationAction.PostNavigationDelayMs) ||
                e.PropertyName == nameof(NavigationAction.Description))
            {
                HasUnsavedChanges = true;
            }
        }

        private void OnSequenceCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Subscribe to new items
            if (e.NewItems != null)
            {
                foreach (KeyboardAction item in e.NewItems)
                {
                    item.PropertyChanged += OnSequenceItemPropertyChanged;
                }
            }

            // Unsubscribe from removed items
            if (e.OldItems != null)
            {
                foreach (KeyboardAction item in e.OldItems)
                {
                    item.PropertyChanged -= OnSequenceItemPropertyChanged;
                }
            }

            HasUnsavedChanges = true;
        }

        private void OnSequenceItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            HasUnsavedChanges = true;
            // Update click markers when sequence changes
            UpdateClickMarkers();
        }

        /// <summary>
        /// Loads the template image from the template service and converts it to a WPF BitmapImage
        /// </summary>
        private async Task LoadTemplateImageAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(SelectedTemplate))
                {
                    TemplateImage = null;
                    TemplateImageWidth = 0;
                    TemplateImageHeight = 0;
                    return;
                }

                var template = await _templateService.LoadTemplateAsync(SelectedTemplate!);
                if (template == null || template.ImageData == null || template.ImageData.Length == 0)
                {
                    TemplateImage = null;
                    TemplateImageWidth = 0;
                    TemplateImageHeight = 0;
                    Status = "Failed to load template image";
                    return;
                }

                // Convert BGR byte array to WPF BitmapSource
                // template.ImageData is raw BGR pixel data (3 bytes per pixel)
                var bitmap = BitmapSource.Create(
                    template.Width,
                    template.Height,
                    96, // DPI X
                    96, // DPI Y
                    PixelFormats.Bgr24, // BGR 24-bit format
                    null, // No palette
                    template.ImageData,
                    template.Width * 3 // Stride: width * bytes per pixel
                );

                bitmap.Freeze(); // Make it thread-safe and improve performance

                TemplateImage = bitmap;
                TemplateImageWidth = template.Width;
                TemplateImageHeight = template.Height;

                await _log.LogDebugAsync($"Loaded template image: {template.Width}x{template.Height}");
            }
            catch (Exception ex)
            {
                await _log.LogErrorAsync($"Failed to load template image: {ex.Message}", ex);
                TemplateImage = null;
                TemplateImageWidth = 0;
                TemplateImageHeight = 0;
                Status = $"Error loading image: {ex.Message}";
            }
        }

        /// <summary>
        /// Updates the click markers collection based on the current navigation sequence
        /// </summary>
        private void UpdateClickMarkers()
        {
            ClickMarkers.Clear();

            if (CurrentNavigation == null || TemplateImageWidth == 0 || TemplateImageHeight == 0)
                return;

            int stepIndex = 1;

            // Add markers for each Click action in the sequence
            foreach (var step in CurrentNavigation.Sequence)
            {
                if (step.Action?.Equals("Click", StringComparison.OrdinalIgnoreCase) == true)
                {
                    ClickMarkers.Add(new ClickMarker
                    {
                        X = step.ClickX * TemplateImageWidth,
                        Y = step.ClickY * TemplateImageHeight,
                        Label = stepIndex.ToString(),
                        Description = step.Description ?? "Click action",
                        MarkerColor = Brushes.DodgerBlue,
                        StepIndex = stepIndex - 1
                    });
                }
                stepIndex++;
            }
        }

        /// <summary>
        /// Replaces the template image with a new image file, allowing region selection
        /// </summary>
        private async Task ReplaceTemplateImageAsync()
        {
            if (string.IsNullOrEmpty(SelectedTemplate))
                return;

            // Warn if there are unsaved navigation changes
            if (HasUnsavedChanges)
            {
                var result = System.Windows.MessageBox.Show(
                    "You have unsaved navigation changes. These changes will not be lost, but they are not yet saved to disk. Continue with image replacement?",
                    "Unsaved Changes",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result != System.Windows.MessageBoxResult.Yes)
                    return;
            }

            // Open file dialog
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All Files (*.*)|*.*",
                Title = "Select New Template Image",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                // Validate the new image
                if (!await ValidateTemplateImageAsync(dialog.FileName))
                {
                    System.Windows.MessageBox.Show(
                        "The selected image is not valid. Please ensure it is a valid image file with reasonable dimensions.",
                        "Invalid Image",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                // Show crop dialog to select region
                var cropViewModel = new ImageCropperDialogViewModel(dialog.FileName, _log);
                var cropDialog = new Views.ImageCropperDialog(cropViewModel);

                // Set owner to the current window (if available)
                cropDialog.Owner = System.Windows.Application.Current.MainWindow;

                var dialogResult = cropDialog.ShowDialog();

                if (dialogResult != true)
                {
                    Status = "Image replacement cancelled";
                    return;
                }

                Status = "Replacing template image...";

                // Replace via service with crop rectangle
                var success = await _templateService.CropAndReplaceTemplateImageAsync(
                    SelectedTemplate!,
                    dialog.FileName,
                    cropViewModel.CropRectangle);

                if (success)
                {
                    // Reload template to show new image
                    await LoadMetadataAsync();
                    Status = "Template image replaced successfully";

                    System.Windows.MessageBox.Show(
                        "Template image has been replaced successfully. You can now test the new image with the existing navigation configuration, and update click points if needed.",
                        "Success",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    Status = "Failed to replace template image";
                    System.Windows.MessageBox.Show(
                        "Failed to replace the template image. Check the logs for details.",
                        "Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                await _log.LogErrorAsync($"Error replacing template image: {ex.Message}", ex);
                Status = $"Error: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"An error occurred while replacing the image:\n{ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Validates a template image file
        /// </summary>
        private async Task<bool> ValidateTemplateImageAsync(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                    return false;

                // Try to load the image
                using var bitmap = new System.Drawing.Bitmap(imagePath);

                // Check dimensions
                if (bitmap.Width < 10 || bitmap.Height < 10)
                {
                    await _log.LogWarningAsync($"Image too small: {bitmap.Width}x{bitmap.Height}");
                    return false;
                }

                if (bitmap.Width > 3840 || bitmap.Height > 2160)
                {
                    await _log.LogWarningAsync($"Image too large: {bitmap.Width}x{bitmap.Height}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                await _log.LogErrorAsync($"Failed to validate image: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Handles canvas click for setting click coordinates in edit mode
        /// </summary>
        public void HandleCanvasClick(double canvasX, double canvasY)
        {
            if (!IsClickEditMode || TemplateImageWidth == 0 || TemplateImageHeight == 0)
                return;

            // Convert canvas coordinates to relative coordinates (0.0-1.0)
            var relativeX = canvasX / TemplateImageWidth;
            var relativeY = canvasY / TemplateImageHeight;

            // Clamp to valid range
            relativeX = Math.Clamp(relativeX, 0.0, 1.0);
            relativeY = Math.Clamp(relativeY, 0.0, 1.0);

            // Find the next Click action in the sequence starting from current index
            KeyboardAction? targetAction = null;
            int searchIndex = _currentEditingClickIndex;

            for (int i = 0; i < CurrentNavigation.Sequence.Count; i++)
            {
                var index = (searchIndex + i) % CurrentNavigation.Sequence.Count;
                var action = CurrentNavigation.Sequence[index];

                if (action.Action?.Equals("Click", StringComparison.OrdinalIgnoreCase) == true)
                {
                    targetAction = action;
                    _currentEditingClickIndex = (index + 1) % CurrentNavigation.Sequence.Count;
                    break;
                }
            }

            if (targetAction != null)
            {
                targetAction.ClickX = relativeX;
                targetAction.ClickY = relativeY;
                HasUnsavedChanges = true;

                Status = $"Updated click point: ({relativeX:F3}, {relativeY:F3}) - Click again to set next point";
            }
            else
            {
                Status = "No Click actions found in sequence - Add a Click action first";
            }
        }

        /// <summary>
        /// Updates the status message based on current mode
        /// </summary>
        private void UpdateStatus()
        {
            if (IsClickEditMode)
            {
                // Count Click actions
                int clickCount = 0;
                foreach (var action in CurrentNavigation.Sequence)
                {
                    if (action.Action?.Equals("Click", StringComparison.OrdinalIgnoreCase) == true)
                        clickCount++;
                }

                if (clickCount > 0)
                {
                    Status = $"Click Edit Mode: Click on the image to set click points ({clickCount} Click action(s) available)";
                    _currentEditingClickIndex = 0; // Reset to first click action
                }
                else
                {
                    Status = "Click Edit Mode: Add Click actions to the sequence first";
                }
            }
            else
            {
                Status = "Click Edit Mode disabled";
            }
        }

        /// <summary>
        /// Opens the Workflow Editor window for creating and managing data-driven workflows
        /// </summary>
        private void OpenWorkflowEditor()
        {
            try
            {
                // Resolve WorkflowEditor window from DI container
                var workflowEditor = _serviceProvider.GetRequiredService<Views.WorkflowEditor>();
                workflowEditor.Show();

                Status = "Workflow Editor opened";
            }
            catch (Exception ex)
            {
                Status = $"Failed to open Workflow Editor: {ex.Message}";
                _ = _log.LogErrorAsync("Failed to open Workflow Editor", ex);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Uses existing RelayCommand and RelayCommandWithParameter in ViewModels/RelayCommand.cs
}

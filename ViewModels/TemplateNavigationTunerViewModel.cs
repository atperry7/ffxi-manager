using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
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

        public ObservableCollection<string> Templates { get; } = new();

        private string? _selectedTemplate;
        public string? SelectedTemplate
        {
            get => _selectedTemplate;
            set { _selectedTemplate = value; OnPropertyChanged(); _ = LoadMetadataAsync(); }
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

        public ICommand RefreshCommand { get; }
        public ICommand TestCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand AddStepCommand { get; }
        public ICommand RemoveStepCommand { get; }

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
            IProcessManagementService processes)
        {
            _templateService = templateService;
            _matchingService = matchingService;
            _automation = automation;
            _screenshots = screenshots;
            _log = log;
            _processes = processes;

            RefreshCommand = new RelayCommand(async () => await LoadTemplatesAsync());
            TestCommand = new RelayCommand(async () => await TestNavigationAsync(), () => CanTest());
            SaveCommand = new RelayCommand(async () => await SaveAsync(), () => CanSave());
            AddStepCommand = new RelayCommand(() => AddStep());
            RemoveStepCommand = new RelayCommandWithParameter<KeyboardAction>(ka => RemoveStep(ka));
            RefreshWindowsCommand = new RelayCommand(async () => await RefreshWindowsAsync());

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
                // Always use Hybrid strategy - tries keyboard first, falls back to click if available
                CurrentNavigation = new NavigationAction
                {
                    Type = NavigationType.Hybrid,
                    PostNavigationDelayMs = Metadata.Navigation.PostNavigationDelayMs,
                    Description = Metadata.Navigation.Description,
                    ClickOffset = Metadata.Navigation.ClickOffset,
                    Fallback = Metadata.Navigation.Fallback,
                    Sequence = new ObservableCollection<KeyboardAction>(Metadata.Navigation.Sequence)
                };
            }
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
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Uses existing RelayCommand and RelayCommandWithParameter in ViewModels/RelayCommand.cs
}

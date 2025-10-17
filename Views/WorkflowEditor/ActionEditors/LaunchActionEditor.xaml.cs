using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FFXIManager.Models;
using FFXIManager.Models.Settings;
using FFXIManager.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace FFXIManager.Views.WorkflowEditor.ActionEditors
{
    /// <summary>
    /// UserControl for editing Launch action parameters.
    /// Binds to KeyboardAction.Parameters dictionary for flexible configuration.
    /// </summary>
    public partial class LaunchActionEditor : UserControl, INotifyPropertyChanged
    {
        private KeyboardAction? _action;
        private List<ExternalApplication> _availableApplications = new();

        public LaunchActionEditor()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            // Note: We don't set DataContext here - WPF will set it to the KeyboardAction
            // when the ContentControl applies the DataTemplate. Our bindings use RelativeSource
            // to bind to this UserControl's properties directly.
        }

        private async void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[LaunchActionEditor] DataContext changed from {e.OldValue?.GetType().Name} to {e.NewValue?.GetType().Name}");

            if (e.NewValue is KeyboardAction action)
            {
                _action = action;

                // Load applications first, then notify bindings
                await LoadAvailableApplications();

                // Notify all bindings AFTER applications are loaded so ComboBox can find selected value
                OnPropertyChanged(nameof(ApplicationName));
                OnPropertyChanged(nameof(AllowSkipIfNotConfigured));
                OnPropertyChanged(nameof(AllowSkipIfRunning));
                OnPropertyChanged(nameof(RetryAttempts));
                OnPropertyChanged(nameof(RetryDelayMs));
                OnPropertyChanged(nameof(TemplatePath));
                OnPropertyChanged(nameof(ConfidenceThreshold));
                OnPropertyChanged(nameof(DelayMs));
                OnPropertyChanged(nameof(Description));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[LaunchActionEditor] DataContext is not a KeyboardAction, skipping load");
            }
        }

        private async Task LoadAvailableApplications()
        {
            try
            {
                // Get the external application service from DI
                var externalAppService = App.Services?.GetService<IExternalApplicationService>();

                if (externalAppService != null)
                {
                    // Load applications from the service
                    var applications = await externalAppService.GetApplicationsAsync();
                    _availableApplications = applications;

                    System.Diagnostics.Debug.WriteLine($"[LaunchActionEditor] Loaded {_availableApplications.Count} applications");
                }
                else
                {
                    // Fallback: Create empty list if service is not available
                    _availableApplications = new List<ExternalApplication>();
                    System.Diagnostics.Debug.WriteLine("[LaunchActionEditor] ExternalApplicationService is null");
                }

                OnPropertyChanged(nameof(AvailableApplications));
            }
            catch (Exception ex)
            {
                // On error, provide empty list
                _availableApplications = new List<ExternalApplication>();
                System.Diagnostics.Debug.WriteLine($"[LaunchActionEditor] Error loading applications: {ex.Message}");
                OnPropertyChanged(nameof(AvailableApplications));
            }
        }

        public List<ExternalApplication> AvailableApplications => _availableApplications;

        // Bind to Parameters dictionary with strong typing
        public string ApplicationName
        {
            get => _action?.GetParameter<string>("ApplicationName", string.Empty) ?? string.Empty;
            set
            {
                if (_action != null)
                {
                    _action.SetParameter("ApplicationName", value);
                    OnPropertyChanged();
                }
            }
        }

        public bool AllowSkipIfNotConfigured
        {
            get => _action?.GetParameter<bool>("AllowSkipIfNotConfigured", false) ?? false;
            set
            {
                if (_action != null)
                {
                    _action.SetParameter("AllowSkipIfNotConfigured", value);
                    OnPropertyChanged();
                }
            }
        }

        public bool AllowSkipIfRunning
        {
            get => _action?.GetParameter<bool>("AllowSkipIfRunning", false) ?? false;
            set
            {
                if (_action != null)
                {
                    _action.SetParameter("AllowSkipIfRunning", value);
                    OnPropertyChanged();
                }
            }
        }

        public int RetryAttempts
        {
            get => _action?.GetParameter<int>("RetryAttempts", 20) ?? 20;
            set
            {
                if (_action != null)
                {
                    _action.SetParameter("RetryAttempts", value);
                    OnPropertyChanged();
                }
            }
        }

        public int RetryDelayMs
        {
            get => _action?.GetParameter<int>("RetryDelayMs", 500) ?? 500;
            set
            {
                if (_action != null)
                {
                    _action.SetParameter("RetryDelayMs", value);
                    OnPropertyChanged();
                }
            }
        }

        public string TemplatePath
        {
            get => _action?.GetParameter<string>("TemplatePath", string.Empty) ?? string.Empty;
            set
            {
                if (_action != null)
                {
                    _action.SetParameter("TemplatePath", value);
                    OnPropertyChanged();
                }
            }
        }

        public float ConfidenceThreshold
        {
            get => _action?.GetParameter<float>("ConfidenceThreshold", 0.8f) ?? 0.8f;
            set
            {
                if (_action != null)
                {
                    _action.SetParameter("ConfidenceThreshold", value);
                    OnPropertyChanged();
                }
            }
        }

        public int DelayMs
        {
            get => _action?.DelayMs ?? 0;
            set
            {
                if (_action != null)
                {
                    _action.DelayMs = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Description
        {
            get => _action?.Description ?? string.Empty;
            set
            {
                if (_action != null)
                {
                    _action.Description = value;
                    OnPropertyChanged();
                }
            }
        }

        private async void BrowseTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open file dialog to select image
                var dialog = new OpenFileDialog
                {
                    Title = "Select Screenshot or Image for Template",
                    Filter = "Image Files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|All Files (*.*)|*.*",
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true)
                    return;

                // Get services from DI
                var loggingService = App.Services?.GetService<ILoggingService>();
                var imageCropService = App.Services?.GetService<IImageCropService>();

                if (loggingService == null || imageCropService == null)
                {
                    System.Windows.MessageBox.Show(
                        "Required services not available. Cannot create template.",
                        "Service Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                await loggingService.LogInfoAsync($"[TEMPLATE-BROWSER] Selected image file: {dialog.FileName}");

                // Open image cropper dialog
                var cropViewModel = new ViewModels.ImageCropperDialogViewModel(dialog.FileName, loggingService);
                var cropDialog = new Views.ImageCropperDialog(cropViewModel);
                cropDialog.Owner = System.Windows.Application.Current.MainWindow;

                var dialogResult = cropDialog.ShowDialog();

                if (dialogResult != true)
                {
                    await loggingService.LogInfoAsync("[TEMPLATE-BROWSER] Image selection cancelled");
                    return;
                }

                // Validate crop rectangle
                if (cropViewModel.CropRectangle == null)
                {
                    await loggingService.LogWarningAsync("[TEMPLATE-BROWSER] No crop rectangle selected");
                    System.Windows.MessageBox.Show(
                        "No crop area was selected.",
                        "Invalid Selection",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Generate unique template name based on timestamp
                var templateName = $"launch_template_{DateTime.Now:yyyyMMdd_HHmmss}";

                // Get template directory path
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                Directory.CreateDirectory(templatesPath);

                var templateFilePath = Path.Combine(templatesPath, $"{templateName}.png");

                await loggingService.LogInfoAsync($"[TEMPLATE-BROWSER] Creating template: {templateFilePath}");

                // Crop and save the image directly to template location
                var success = await imageCropService.CropImageAsync(
                    dialog.FileName,
                    cropViewModel.CropRectangle.Value,
                    templateFilePath);

                if (success)
                {
                    // Update the template path (stored without extension or directory)
                    TemplatePath = templateName;
                    await loggingService.LogInfoAsync($"[TEMPLATE-BROWSER] Template created successfully: {templateName}");

                    System.Windows.MessageBox.Show(
                        $"Template '{templateName}' created successfully",
                        "Template Created",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        "Failed to create template image. Check logs for details.",
                        "Template Creation Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TEMPLATE-BROWSER] Error: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"Failed to create template: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

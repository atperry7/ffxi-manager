using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.ViewModels.WorkflowEditor
{
    /// <summary>
    /// Manages template image operations for the Workflow Editor.
    /// Handles template selection, replacement, preview, and thumbnail loading
    /// for both step-level and action-level templates.
    /// </summary>
    public class WorkflowEditorTemplateManager
    {
        private readonly ITemplateManagementService _templateService;
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILoggingService _loggingService;
        private readonly IDialogService _dialogService;

        public WorkflowEditorTemplateManager(
            ITemplateManagementService templateService,
            IScreenshotCaptureService screenshotService,
            IServiceProvider serviceProvider,
            ILoggingService loggingService,
            IDialogService dialogService)
        {
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        }

        #region Step-Level Template Operations

        /// <summary>
        /// Creates a new template from cropped image for a step
        /// </summary>
        public async Task<bool> CreateStepTemplateAsync(WorkflowStepDefinition step, string sourceImagePath, Rectangle cropRectangle)
        {
            if (step == null || string.IsNullOrWhiteSpace(sourceImagePath))
                return false;

            try
            {
                // Generate unique template name based on step ID
                var templateName = $"step_{step.StepId.ToLowerInvariant().Replace("-", "_")}";

                // Get template directory path
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                Directory.CreateDirectory(templatesPath);

                var templateFilePath = Path.Combine(templatesPath, $"{templateName}.png");

                await _loggingService.LogInfoAsync($"Creating template: {templateName} for step '{step.DisplayName}'");

                // Get image crop service from DI
                var imageCropService = _serviceProvider.GetService(typeof(IImageCropService)) as IImageCropService;

                if (imageCropService == null)
                {
                    await _loggingService.LogErrorAsync("IImageCropService not available from DI");
                    await _dialogService.ShowMessageDialogAsync("Service Error", "Image crop service not available.");
                    return false;
                }

                // Crop and save the image
                var success = await imageCropService.CropImageAsync(sourceImagePath, cropRectangle, templateFilePath);

                if (success)
                {
                    // Update step with new template path (stored without extension)
                    step.TemplatePath = templateName;
                    await _loggingService.LogInfoAsync($"Template created successfully: {templateName}");
                    await _dialogService.ShowMessageDialogAsync("Template Created", $"Template '{templateName}' created successfully");
                    return true;
                }
                else
                {
                    await _dialogService.ShowMessageDialogAsync("Template Creation Failed", "Failed to create template image. Check logs for details.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error creating step template", ex);
                await _dialogService.ShowMessageDialogAsync("Creation Failed", $"Failed to create template: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Replaces an existing step template with a new cropped image
        /// </summary>
        public async Task<bool> ReplaceStepTemplateAsync(WorkflowStepDefinition step, string sourceImagePath, Rectangle? cropRectangle)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.TemplatePath) || string.IsNullOrWhiteSpace(sourceImagePath))
                return false;

            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Replace Template Image",
                    $"Replace the template image for '{step.DisplayName}'?\n\nThis will update: {step.TemplatePath}");

                if (!result) return false;

                await _loggingService.LogInfoAsync($"Replacing template: {step.TemplatePath}");

                // Replace template image with cropped version
                var success = await _templateService.CropAndReplaceTemplateImageAsync(
                    step.TemplatePath,
                    sourceImagePath,
                    cropRectangle);

                if (success)
                {
                    await _loggingService.LogInfoAsync($"Template replaced successfully: {step.TemplatePath}");
                    await _dialogService.ShowMessageDialogAsync("Template Replaced", $"Template image for '{step.DisplayName}' replaced successfully");
                    return true;
                }
                else
                {
                    await _dialogService.ShowMessageDialogAsync("Template Replacement Failed", "Failed to replace template image. Check logs for details.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error replacing step template", ex);
                await _dialogService.ShowMessageDialogAsync("Replacement Failed", $"Failed to replace template: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loads the template thumbnail image for a step
        /// </summary>
        public BitmapImage? LoadStepTemplateThumbnail(WorkflowStepDefinition? step)
        {
            try
            {
                if (step == null || string.IsNullOrWhiteSpace(step.TemplatePath))
                    return null;

                // Construct template file path
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFileName = step.TemplatePath.EndsWith(".png") ? step.TemplatePath : $"{step.TemplatePath}.png";
                var templateFilePath = Path.Combine(templatesPath, templateFileName);

                if (!File.Exists(templateFilePath))
                {
                    _ = _loggingService.LogDebugAsync($"Template file not found: {templateFilePath}");
                    return null;
                }

                // Load image with BitmapCacheOption.OnLoad to avoid file locking
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(templateFilePath, UriKind.Absolute);
                bitmap.DecodePixelHeight = 100; // Thumbnail height
                bitmap.EndInit();
                bitmap.Freeze(); // Make it thread-safe

                _ = _loggingService.LogDebugAsync($"Loaded template thumbnail: {templateFileName}");
                return bitmap;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error loading step template thumbnail", ex);
                return null;
            }
        }

        /// <summary>
        /// Shows the step template image in a large popup window
        /// </summary>
        public async Task ShowLargeStepTemplateAsync(WorkflowStepDefinition step)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.TemplatePath))
                return;

            try
            {
                // Construct template file path
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFileName = step.TemplatePath.EndsWith(".png") ? step.TemplatePath : $"{step.TemplatePath}.png";
                var templateFilePath = Path.Combine(templatesPath, templateFileName);

                if (!File.Exists(templateFilePath))
                {
                    await _dialogService.ShowMessageDialogAsync("Template Not Found", $"Template file not found:\n{templateFilePath}");
                    return;
                }

                // Create a simple window to display the image
                var window = new Window
                {
                    Title = $"Template Preview: {step.DisplayName}",
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current?.MainWindow,
                    Content = new System.Windows.Controls.Image
                    {
                        Source = new BitmapImage(new Uri(templateFilePath, UriKind.Absolute)),
                        Stretch = Stretch.Uniform
                    }
                };

                window.ShowDialog();
                await _loggingService.LogDebugAsync($"Showed large template image: {templateFileName}");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error showing large step template", ex);
                await _dialogService.ShowMessageDialogAsync("Display Error", $"Failed to display template image: {ex.Message}");
            }
        }

        #endregion

        #region Action-Level Template Operations

        /// <summary>
        /// Generates a unique template name for an action based on step and action index
        /// </summary>
        public string GenerateActionTemplateName(WorkflowStepDefinition? step, KeyboardAction? action)
        {
            var stepId = step?.StepId ?? "step";
            int index = 0;
            if (step?.Navigation?.Sequence != null && action != null)
            {
                index = step.Navigation.Sequence.IndexOf(action);
            }
            return $"action_{stepId}_{index}".ToLowerInvariant();
        }

        /// <summary>
        /// Creates a new template from cropped image for an action
        /// </summary>
        public async Task<bool> CreateActionTemplateAsync(KeyboardAction action, WorkflowStepDefinition? step, string sourceImagePath, Rectangle cropRectangle)
        {
            if (action == null || string.IsNullOrWhiteSpace(sourceImagePath))
                return false;

            try
            {
                var templateName = GenerateActionTemplateName(step, action);
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                Directory.CreateDirectory(templatesPath);
                var templateFilePath = Path.Combine(templatesPath, $"{templateName}.png");

                var imageCropService = _serviceProvider.GetService(typeof(IImageCropService)) as IImageCropService;
                if (imageCropService == null)
                {
                    await _dialogService.ShowMessageDialogAsync("Service Error", "Image crop service not available.");
                    return false;
                }

                var success = await imageCropService.CropImageAsync(sourceImagePath, cropRectangle, templateFilePath);

                if (success)
                {
                    action.SetParameter("TemplatePath", templateName);
                    return true;
                }
                else
                {
                    await _dialogService.ShowMessageDialogAsync("Template Creation Failed", "Failed to create template image.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error creating action template", ex);
                return false;
            }
        }

        /// <summary>
        /// Replaces an existing action template with a new cropped image
        /// </summary>
        public async Task<bool> ReplaceActionTemplateAsync(KeyboardAction action, string sourceImagePath, Rectangle? cropRectangle)
        {
            if (action == null)
                return false;

            try
            {
                var actionTemplate = action.GetParameter<string>("TemplatePath", string.Empty);
                if (string.IsNullOrWhiteSpace(actionTemplate))
                    return false;

                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Replace Action Template Image",
                    $"Replace the template image for action '{action.Action}'?\n\nThis will update: {actionTemplate}");

                if (!result) return false;

                var success = await _templateService.CropAndReplaceTemplateImageAsync(actionTemplate, sourceImagePath, cropRectangle);
                return success;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error replacing action template", ex);
                return false;
            }
        }

        /// <summary>
        /// Loads the template thumbnail image for an action
        /// </summary>
        public BitmapImage? LoadActionTemplateThumbnail(KeyboardAction? action)
        {
            try
            {
                if (action == null)
                    return null;

                var actionTemplate = action.GetParameter<string>("TemplatePath", string.Empty);
                if (string.IsNullOrWhiteSpace(actionTemplate))
                    return null;

                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFileName = actionTemplate.EndsWith(".png") ? actionTemplate : $"{actionTemplate}.png";
                var templateFilePath = Path.Combine(templatesPath, templateFileName);

                if (!File.Exists(templateFilePath))
                {
                    _ = _loggingService.LogDebugAsync($"Action template file not found: {templateFilePath}");
                    return null;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(templateFilePath, UriKind.Absolute);
                bitmap.DecodePixelHeight = 100;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error loading action template thumbnail", ex);
                return null;
            }
        }

        /// <summary>
        /// Shows the action template image in a large popup window
        /// </summary>
        public async Task ShowLargeActionTemplateAsync(KeyboardAction action)
        {
            if (action == null)
                return;

            try
            {
                var actionTemplate = action.GetParameter<string>("TemplatePath", string.Empty);
                if (string.IsNullOrWhiteSpace(actionTemplate)) return;

                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFileName = actionTemplate.EndsWith(".png") ? actionTemplate : $"{actionTemplate}.png";
                var templateFilePath = Path.Combine(templatesPath, templateFileName);

                if (!File.Exists(templateFilePath))
                {
                    await _dialogService.ShowMessageDialogAsync("Template Not Found", $"Template file not found:\n{templateFilePath}");
                    return;
                }

                var window = new Window
                {
                    Title = $"Action Template Preview: {action.Action}",
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current?.MainWindow,
                    Content = new System.Windows.Controls.Image
                    {
                        Source = new BitmapImage(new Uri(templateFilePath, UriKind.Absolute)),
                        Stretch = Stretch.Uniform
                    }
                };

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error showing action template", ex);
            }
        }

        #endregion
    }
}

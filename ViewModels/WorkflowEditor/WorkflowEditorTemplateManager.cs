using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

                try
                {
                    var template = _templateService.LoadTemplateAsync(step.TemplatePath).GetAwaiter().GetResult();
                    if (template != null && template.ImageData != null)
                    {
                        using var ms = new System.IO.MemoryStream(template.ImageData);
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.DecodePixelHeight = 100;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                }
                catch { /* fallback below */ }

                // Fallback: direct file load if service path failed
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFileName = step.TemplatePath.EndsWith(".png") ? step.TemplatePath : $"{step.TemplatePath}.png";
                var filePath = Path.Combine(templatesPath, templateFileName);
                if (!File.Exists(filePath)) return null;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.DecodePixelHeight = 100;
                bitmap.EndInit();
                bitmap.Freeze();
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
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFileName = step.TemplatePath.EndsWith(".png") ? step.TemplatePath : $"{step.TemplatePath}.png";
                var templateFilePath = Path.Combine(templatesPath, templateFileName);
                if (!File.Exists(templateFilePath))
                {
                    await _dialogService.ShowMessageDialogAsync("Template Not Found", $"Template file not found:\n{templateFilePath}");
                    return;
                }

                // Try to use the rich Template Viewer with click markers
                var vm = _serviceProvider.GetService(typeof(FFXIManager.ViewModels.TemplateViewerDialogViewModel)) as FFXIManager.ViewModels.TemplateViewerDialogViewModel;
                var dlg = _serviceProvider.GetService(typeof(FFXIManager.Views.TemplateViewerDialog)) as Window;

                if (vm != null && dlg is FFXIManager.Views.TemplateViewerDialog typedDlg)
                {
                    await vm.LoadTemplateAsync(templateFileName, step.Navigation?.Sequence);
                    typedDlg.Owner = Application.Current?.MainWindow;
                    // Ensure the dialog uses the prepared VM
                    typedDlg.DataContext = vm;
                    typedDlg.Title = $"Template Preview: {step.DisplayName}";
                    typedDlg.ShowDialog();
                    await _loggingService.LogDebugAsync($"Showed template viewer with markers: {templateFileName}");
                }
                else
                {
                    // Fallback: simple window
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
                    await _loggingService.LogDebugAsync($"Showed large template image (fallback): {templateFileName}");
                }
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

                try
                {
                    var template = _templateService.LoadTemplateAsync(actionTemplate).GetAwaiter().GetResult();
                    if (template != null && template.ImageData != null)
                    {
                        using var ms = new System.IO.MemoryStream(template.ImageData);
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.DecodePixelHeight = 100;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                }
                catch { /* fallback below */ }

                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFileName = actionTemplate.EndsWith(".png") ? actionTemplate : $"{actionTemplate}.png";
                var filePath = Path.Combine(templatesPath, templateFileName);
                if (!File.Exists(filePath)) return null;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
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

                // Try to use the rich Template Viewer with a marker for this action
                var vm = _serviceProvider.GetService(typeof(FFXIManager.ViewModels.TemplateViewerDialogViewModel)) as FFXIManager.ViewModels.TemplateViewerDialogViewModel;
                var dlg = _serviceProvider.GetService(typeof(FFXIManager.Views.TemplateViewerDialog)) as Window;

                if (vm != null && dlg is FFXIManager.Views.TemplateViewerDialog typedDlg)
                {
                    var single = new System.Collections.ObjectModel.ObservableCollection<KeyboardAction> { action };
                    await vm.LoadTemplateAsync(templateFileName, single);
                    typedDlg.Owner = Application.Current?.MainWindow;
                    typedDlg.DataContext = vm;
                    typedDlg.Title = $"Action Template Preview: {action.Action}";
                    typedDlg.ShowDialog();
                    await _loggingService.LogDebugAsync($"Showed action template viewer with marker: {templateFileName}");
                }
                else
                {
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
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error showing action template", ex);
            }
        }

        /// <summary>
        /// Opens the template viewer in pick mode to select click points.
        /// Prefers the action-level template; falls back to step-level template if needed.
        /// Applies chosen coordinates to the action's ClickPoints parameter.
        /// All click configuration (FromCenter mode, coordinates, etc.) is managed within the dialog.
        /// </summary>
        public async Task<bool> PickClickPositionForActionAsync(WorkflowStepDefinition? step, KeyboardAction action)
        {
            if (action == null)
                return false;

            try
            {
                // Prefer action-level template if provided; fallback to step-level template
                var actionTemplate = action.GetParameter<string>("TemplatePath", string.Empty);
                string? templateFileName = !string.IsNullOrWhiteSpace(actionTemplate)
                    ? (actionTemplate.EndsWith(".png") ? actionTemplate : $"{actionTemplate}.png")
                    : (!string.IsNullOrWhiteSpace(step?.TemplatePath) ? (step!.TemplatePath.EndsWith(".png") ? step!.TemplatePath : $"{step!.TemplatePath}.png") : null);

                if (string.IsNullOrWhiteSpace(templateFileName))
                {
                    await _dialogService.ShowMessageDialogAsync("No Template", "No template image available to pick from. Add a template to the action or step first.");
                    return false;
                }

                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFilePath = Path.Combine(templatesPath, templateFileName);
                if (!File.Exists(templateFilePath))
                {
                    await _dialogService.ShowMessageDialogAsync("Template Not Found", $"Template file not found:\n{templateFilePath}");
                    return false;
                }

                var vm = _serviceProvider.GetService(typeof(FFXIManager.ViewModels.TemplateViewerDialogViewModel)) as FFXIManager.ViewModels.TemplateViewerDialogViewModel;
                var dlg = _serviceProvider.GetService(typeof(FFXIManager.Views.TemplateViewerDialog)) as Window;
                if (vm == null || dlg is not FFXIManager.Views.TemplateViewerDialog typedDlg)
                {
                    await _dialogService.ShowMessageDialogAsync("Service Error", "Template viewer is not available.");
                    return false;
                }

                // Multi-pick mode: append points, allow clear-all, then persist all points on Apply
                vm.IsPickMode = true;
                vm.IsMultiPickMode = true;
                vm.MultiPickCount = 1; // start with 1; user clicks will append
                vm.ResetMultiPick();

                await vm.LoadTemplateAsync(templateFilePath, null);
                vm.ClickMarkers.Clear();

                // Load existing click points from action parameters
                var existing = action.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new System.Collections.Generic.List<RelativeClickOffset>())
                               ?? new System.Collections.Generic.List<RelativeClickOffset>();

                // Set FromCenterMode based on first click point (or default to false)
                vm.FromCenterMode = existing.Count > 0 && existing[0].FromCenter;

                for (int i = 0; i < existing.Count; i++)
                {
                    // Convert coordinates to pixel positions based on mode
                    double pixelX, pixelY;
                    if (existing[i].FromCenter)
                    {
                        // Center-relative (-0.5 to 0.5) to pixel coordinates
                        pixelX = (existing[i].X + 0.5) * vm.TemplateImageWidth;
                        pixelY = (existing[i].Y + 0.5) * vm.TemplateImageHeight;
                    }
                    else
                    {
                        // Template-relative (0.0 to 1.0) to pixel coordinates
                        pixelX = existing[i].X * vm.TemplateImageWidth;
                        pixelY = existing[i].Y * vm.TemplateImageHeight;
                    }

                    vm.ClickMarkers.Add(new ClickMarker
                    {
                        X = pixelX,
                        Y = pixelY,
                        RelativeX = existing[i].X,
                        RelativeY = existing[i].Y,
                        Label = (i + 1).ToString(),
                        Description = existing[i].Description ?? $"Click {i + 1}",
                        MarkerColor = System.Windows.Media.Brushes.DodgerBlue,
                        StepIndex = i
                    });
                }

                typedDlg.Owner = Application.Current?.MainWindow;
                typedDlg.DataContext = vm;
                typedDlg.Title = "Pick Click Points";

                var result = typedDlg.ShowDialog();
                if (result == true)
                {
                    // Persist all markers to ClickPoints as normalized offsets
                    var list = new System.Collections.Generic.List<RelativeClickOffset>();
                    foreach (var m in vm.ClickMarkers)
                    {
                        double nx, ny;
                        if (vm.FromCenterMode)
                        {
                            // Pixel to center-relative (-0.5 to 0.5)
                            nx = vm.TemplateImageWidth > 0 ? (m.X / vm.TemplateImageWidth) - 0.5 : 0.0;
                            ny = vm.TemplateImageHeight > 0 ? (m.Y / vm.TemplateImageHeight) - 0.5 : 0.0;
                        }
                        else
                        {
                            // Pixel to template-relative (0.0 to 1.0)
                            nx = vm.TemplateImageWidth > 0 ? m.X / vm.TemplateImageWidth : 0.5;
                            ny = vm.TemplateImageHeight > 0 ? m.Y / vm.TemplateImageHeight : 0.5;
                        }
                        list.Add(new RelativeClickOffset
                        {
                            X = nx,
                            Y = ny,
                            Description = m.Description,
                            FromCenter = vm.FromCenterMode
                        });
                    }
                    action.SetParameter("ClickPoints", list);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error picking click position", ex);
                return false;
            }
        }

        /// <summary>
        /// Opens the template viewer in multi-pick mode to select four click positions
        /// for PlayOnline member slots 1-4. Applies chosen coordinates to the step's
        /// MemberSlot*Click properties.
        /// </summary>
        public async Task<bool> PickMemberSlotClickPositionsForActionAsync(WorkflowStepDefinition step, KeyboardAction action)
        {
            if (step == null || action == null)
                return false;

            try
            {
                // Prefer action-level template if provided; fallback to step-level template
                var actionTemplate = action.GetParameter<string>("TemplatePath", string.Empty);
                var preferred = !string.IsNullOrWhiteSpace(actionTemplate) ? actionTemplate : step.TemplatePath;
                if (string.IsNullOrWhiteSpace(preferred))
                {
                    await _dialogService.ShowMessageDialogAsync("No Template", "Add a template to the MemberSlot action or the step before setting slot click points.");
                    return false;
                }

                var templateFileName = preferred.EndsWith(".png") ? preferred : $"{preferred}.png";
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFilePath = Path.Combine(templatesPath, templateFileName);

                if (!File.Exists(templateFilePath))
                {
                    await _dialogService.ShowMessageDialogAsync("Template Not Found", $"Template file not found:\n{templateFilePath}");
                    return false;
                }

                var vm = _serviceProvider.GetService(typeof(FFXIManager.ViewModels.TemplateViewerDialogViewModel)) as FFXIManager.ViewModels.TemplateViewerDialogViewModel;
                var dlg = _serviceProvider.GetService(typeof(FFXIManager.Views.TemplateViewerDialog)) as Window;

                if (vm == null || dlg is not FFXIManager.Views.TemplateViewerDialog typedDlg)
                {
                    await _dialogService.ShowMessageDialogAsync("Service Error", "Template viewer is not available.");
                    return false;
                }

                // Prepare viewmodel
                vm.IsPickMode = true;
                vm.IsMultiPickMode = true;
                vm.MultiPickCount = 4;
                vm.ResetMultiPick();

                // Preload any existing markers
                await vm.LoadTemplateAsync(templateFileName, null);
                vm.ClickMarkers.Clear();

                var points = action.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new System.Collections.Generic.List<RelativeClickOffset>())
                             ?? new System.Collections.Generic.List<RelativeClickOffset>();
                // Ensure at least 4 placeholders
                while (points.Count < 4) points.Add(new RelativeClickOffset { X = 0.5, Y = 0.5, Description = $"Slot {points.Count + 1}", FromCenter = true });

                // Default to center-relative mode (recommended for POL member slots)
                // Check if existing points use center-relative mode; preserve if set
                vm.FromCenterMode = points.Count > 0 ? points[0].FromCenter : true;

                for (int i = 0; i < 4; i++)
                {
                    var color = i switch
                    {
                        0 => System.Windows.Media.Brushes.Red,
                        1 => System.Windows.Media.Brushes.DodgerBlue,
                        2 => System.Windows.Media.Brushes.Orange,
                        _ => System.Windows.Media.Brushes.LimeGreen
                    };

                    double px = (points[i].X) * vm.TemplateImageWidth;
                    double py = (points[i].Y) * vm.TemplateImageHeight;
                    vm.ClickMarkers.Add(new ClickMarker
                    {
                        X = px,
                        Y = py,
                        Label = (i + 1).ToString(),
                        Description = $"Slot {i + 1}",
                        MarkerColor = color,
                        StepIndex = i
                    });
                }

                // Live update to step as picks occur
                void OnMultiPick(int index, double x, double y)
                {
                    if (index >= 0 && index < 4)
                    {
                        points[index] = new RelativeClickOffset { X = x, Y = y, Description = $"Slot {index + 1}", FromCenter = vm.FromCenterMode };
                    }
                }

                vm.MultiPickChanged += OnMultiPick;
                typedDlg.Owner = Application.Current?.MainWindow;
                typedDlg.DataContext = vm;
                typedDlg.Title = "Pick Member Slot Click Points";

                var result = typedDlg.ShowDialog();
                vm.MultiPickChanged -= OnMultiPick;

                if (result == true)
                {
                    // Persist 4 points directly as an array in parameters
                    action.SetParameter("ClickPoints", points);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error picking member slot click positions", ex);
                return false;
            }
        }

        /// <summary>
        /// Shows the step template with the configured member slot click points overlaid.
        /// </summary>
        public async Task ShowMemberSlotClickPositionsAsync(WorkflowStepDefinition step, KeyboardAction action)
        {
            if (step == null || action == null) return;
            try
            {
                if (string.IsNullOrWhiteSpace(step.TemplatePath))
                {
                    await _dialogService.ShowMessageDialogAsync("No Template", "Add a step template to preview click points.");
                    return;
                }

                var actionTemplate = action.GetParameter<string>("TemplatePath", string.Empty);
                var preferred = !string.IsNullOrWhiteSpace(actionTemplate) ? actionTemplate : step.TemplatePath;
                var templateFileName = preferred.EndsWith(".png") ? preferred : $"{preferred}.png";
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFilePath = Path.Combine(templatesPath, templateFileName);

                if (!File.Exists(templateFilePath))
                {
                    await _dialogService.ShowMessageDialogAsync("Template Not Found", $"Template file not found:\n{templateFilePath}");
                    return;
                }

                var vm = _serviceProvider.GetService(typeof(FFXIManager.ViewModels.TemplateViewerDialogViewModel)) as FFXIManager.ViewModels.TemplateViewerDialogViewModel;
                var dlg = _serviceProvider.GetService(typeof(FFXIManager.Views.TemplateViewerDialog)) as Window;

                if (vm == null || dlg is not FFXIManager.Views.TemplateViewerDialog typedDlg)
                {
                    await _dialogService.ShowMessageDialogAsync("Service Error", "Template viewer is not available.");
                    return;
                }

                vm.IsPickMode = false;
                await vm.LoadTemplateAsync(templateFileName, null);
                vm.ClickMarkers.Clear();

                var points = action.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new System.Collections.Generic.List<RelativeClickOffset>())
                             ?? new System.Collections.Generic.List<RelativeClickOffset>();
                while (points.Count < 4) points.Add(new RelativeClickOffset { X = 0.5, Y = 0.5, Description = $"Slot {points.Count + 1}" });

                for (int i = 0; i < 4; i++)
                {
                    var color = i switch
                    {
                        0 => System.Windows.Media.Brushes.Red,
                        1 => System.Windows.Media.Brushes.DodgerBlue,
                        2 => System.Windows.Media.Brushes.Orange,
                        _ => System.Windows.Media.Brushes.LimeGreen
                    };
                    double px = (points[i].X) * vm.TemplateImageWidth;
                    double py = (points[i].Y) * vm.TemplateImageHeight;
                    vm.ClickMarkers.Add(new ClickMarker
                    {
                        X = px,
                        Y = py,
                        Label = (i + 1).ToString(),
                        Description = $"Slot {i + 1}",
                        MarkerColor = color,
                        StepIndex = i
                    });
                }

                typedDlg.Owner = Application.Current?.MainWindow;
                typedDlg.DataContext = vm;
                typedDlg.Title = "Member Slot Click Points";
                typedDlg.ShowDialog();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error showing member slot click positions", ex);
            }
        }

        /// <summary>
        /// Opens the template viewer in multi-pick mode to select sixteen click positions
        /// for FFXI character slots 1-16. Applies chosen coordinates to the action's
        /// ClickPoints parameter.
        /// </summary>
        public async Task<bool> PickCharacterSlotClickPositionsForActionAsync(WorkflowStepDefinition step, KeyboardAction action)
        {
            if (step == null || action == null)
                return false;

            try
            {
                // Prefer action-level template if provided; fallback to step-level template
                var actionTemplate = action.GetParameter<string>("TemplatePath", string.Empty);
                var preferred = !string.IsNullOrWhiteSpace(actionTemplate) ? actionTemplate : step.TemplatePath;
                if (string.IsNullOrWhiteSpace(preferred))
                {
                    await _dialogService.ShowMessageDialogAsync("No Template", "Add a template to the CharacterSlot action or the step before setting slot click points.");
                    return false;
                }

                var templateFileName = preferred.EndsWith(".png") ? preferred : $"{preferred}.png";
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFilePath = Path.Combine(templatesPath, templateFileName);

                if (!File.Exists(templateFilePath))
                {
                    await _dialogService.ShowMessageDialogAsync("Template Not Found", $"Template file not found:\n{templateFilePath}");
                    return false;
                }

                var vm = _serviceProvider.GetService(typeof(FFXIManager.ViewModels.TemplateViewerDialogViewModel)) as FFXIManager.ViewModels.TemplateViewerDialogViewModel;
                var dlg = _serviceProvider.GetService(typeof(FFXIManager.Views.TemplateViewerDialog)) as Window;

                if (vm == null || dlg is not FFXIManager.Views.TemplateViewerDialog typedDlg)
                {
                    await _dialogService.ShowMessageDialogAsync("Service Error", "Template viewer is not available.");
                    return false;
                }

                // Prepare viewmodel
                vm.IsPickMode = true;
                vm.IsMultiPickMode = true;
                vm.MultiPickCount = 16;
                vm.ResetMultiPick();

                // Preload any existing markers
                await vm.LoadTemplateAsync(templateFileName, null);
                vm.ClickMarkers.Clear();

                var points = action.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new System.Collections.Generic.List<RelativeClickOffset>())
                             ?? new System.Collections.Generic.List<RelativeClickOffset>();
                // Ensure at least 16 placeholders
                while (points.Count < 16) points.Add(new RelativeClickOffset { X = 0.5, Y = 0.5, Description = $"Slot {points.Count + 1}", FromCenter = true });

                // Default to center-relative mode (recommended for FFXI character slots)
                // Check if existing points use center-relative mode; preserve if set
                vm.FromCenterMode = points.Count > 0 ? points[0].FromCenter : true;

                for (int i = 0; i < 16; i++)
                {
                    // Use extended color palette from TemplateViewerDialog
                    var color = (i % 16) switch
                    {
                        0 => System.Windows.Media.Brushes.Red,
                        1 => System.Windows.Media.Brushes.DodgerBlue,
                        2 => System.Windows.Media.Brushes.Orange,
                        3 => System.Windows.Media.Brushes.LimeGreen,
                        4 => System.Windows.Media.Brushes.Purple,
                        5 => System.Windows.Media.Brushes.DeepPink,
                        6 => System.Windows.Media.Brushes.Cyan,
                        7 => System.Windows.Media.Brushes.Gold,
                        8 => System.Windows.Media.Brushes.Crimson,
                        9 => System.Windows.Media.Brushes.RoyalBlue,
                        10 => System.Windows.Media.Brushes.DarkOrange,
                        11 => System.Windows.Media.Brushes.ForestGreen,
                        12 => System.Windows.Media.Brushes.MediumPurple,
                        13 => System.Windows.Media.Brushes.HotPink,
                        14 => System.Windows.Media.Brushes.Teal,
                        15 => System.Windows.Media.Brushes.Yellow,
                        _ => System.Windows.Media.Brushes.Gray
                    };

                    double px = (points[i].X) * vm.TemplateImageWidth;
                    double py = (points[i].Y) * vm.TemplateImageHeight;
                    vm.ClickMarkers.Add(new ClickMarker
                    {
                        X = px,
                        Y = py,
                        Label = (i + 1).ToString(),
                        Description = $"Slot {i + 1}",
                        MarkerColor = color,
                        StepIndex = i
                    });
                }

                // Live update to action as picks occur
                void OnMultiPick(int index, double x, double y)
                {
                    if (index >= 0 && index < 16)
                    {
                        points[index] = new RelativeClickOffset { X = x, Y = y, Description = $"Slot {index + 1}", FromCenter = vm.FromCenterMode };
                    }
                }

                vm.MultiPickChanged += OnMultiPick;
                typedDlg.Owner = Application.Current?.MainWindow;
                typedDlg.DataContext = vm;
                typedDlg.Title = "Pick Character Slot Click Points";

                var result = typedDlg.ShowDialog();
                vm.MultiPickChanged -= OnMultiPick;

                if (result == true)
                {
                    // Persist 16 points directly as an array in parameters
                    action.SetParameter("ClickPoints", points);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error picking character slot click positions", ex);
                return false;
            }
        }

        /// <summary>
        /// Shows the step template with the configured character slot click points overlaid.
        /// </summary>
        public async Task ShowCharacterSlotClickPositionsAsync(WorkflowStepDefinition step, KeyboardAction action)
        {
            if (step == null || action == null) return;
            try
            {
                if (string.IsNullOrWhiteSpace(step.TemplatePath))
                {
                    await _dialogService.ShowMessageDialogAsync("No Template", "Add a step template to preview click points.");
                    return;
                }

                var actionTemplate = action.GetParameter<string>("TemplatePath", string.Empty);
                var preferred = !string.IsNullOrWhiteSpace(actionTemplate) ? actionTemplate : step.TemplatePath;
                var templateFileName = preferred.EndsWith(".png") ? preferred : $"{preferred}.png";
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFilePath = Path.Combine(templatesPath, templateFileName);

                if (!File.Exists(templateFilePath))
                {
                    await _dialogService.ShowMessageDialogAsync("Template Not Found", $"Template file not found:\n{templateFilePath}");
                    return;
                }

                var vm = _serviceProvider.GetService(typeof(FFXIManager.ViewModels.TemplateViewerDialogViewModel)) as FFXIManager.ViewModels.TemplateViewerDialogViewModel;
                var dlg = _serviceProvider.GetService(typeof(FFXIManager.Views.TemplateViewerDialog)) as Window;

                if (vm == null || dlg is not FFXIManager.Views.TemplateViewerDialog typedDlg)
                {
                    await _dialogService.ShowMessageDialogAsync("Service Error", "Template viewer is not available.");
                    return;
                }

                vm.IsPickMode = false;
                await vm.LoadTemplateAsync(templateFileName, null);
                vm.ClickMarkers.Clear();

                var points = action.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new System.Collections.Generic.List<RelativeClickOffset>())
                             ?? new System.Collections.Generic.List<RelativeClickOffset>();
                while (points.Count < 16) points.Add(new RelativeClickOffset { X = 0.5, Y = 0.5, Description = $"Slot {points.Count + 1}" });

                for (int i = 0; i < 16; i++)
                {
                    // Use extended color palette
                    var color = (i % 16) switch
                    {
                        0 => System.Windows.Media.Brushes.Red,
                        1 => System.Windows.Media.Brushes.DodgerBlue,
                        2 => System.Windows.Media.Brushes.Orange,
                        3 => System.Windows.Media.Brushes.LimeGreen,
                        4 => System.Windows.Media.Brushes.Purple,
                        5 => System.Windows.Media.Brushes.DeepPink,
                        6 => System.Windows.Media.Brushes.Cyan,
                        7 => System.Windows.Media.Brushes.Gold,
                        8 => System.Windows.Media.Brushes.Crimson,
                        9 => System.Windows.Media.Brushes.RoyalBlue,
                        10 => System.Windows.Media.Brushes.DarkOrange,
                        11 => System.Windows.Media.Brushes.ForestGreen,
                        12 => System.Windows.Media.Brushes.MediumPurple,
                        13 => System.Windows.Media.Brushes.HotPink,
                        14 => System.Windows.Media.Brushes.Teal,
                        15 => System.Windows.Media.Brushes.Yellow,
                        _ => System.Windows.Media.Brushes.Gray
                    };
                    double px = (points[i].X) * vm.TemplateImageWidth;
                    double py = (points[i].Y) * vm.TemplateImageHeight;
                    vm.ClickMarkers.Add(new ClickMarker
                    {
                        X = px,
                        Y = py,
                        Label = (i + 1).ToString(),
                        Description = $"Slot {i + 1}",
                        MarkerColor = color,
                        StepIndex = i
                    });
                }

                typedDlg.Owner = Application.Current?.MainWindow;
                typedDlg.DataContext = vm;
                typedDlg.Title = "Character Slot Click Points";
                typedDlg.ShowDialog();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error showing character slot click positions", ex);
            }
        }

        #endregion
    }
}

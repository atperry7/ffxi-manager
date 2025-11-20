using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.ViewModels;
using FFXIManager.Views;
using Microsoft.Win32;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Coordinates dialog interactions for template selection, cropping, and click point configuration.
    /// Extracts dialog lifecycle management from ViewModels for better separation of concerns.
    /// </summary>
    public class TemplateDialogCoordinationService : ITemplateDialogCoordinationService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILoggingService _loggingService;
        private readonly IImageCropService _imageCropService;
        private readonly ITemplateManagementService _templateService;

        public TemplateDialogCoordinationService(
            IServiceProvider serviceProvider,
            ILoggingService loggingService,
            IImageCropService imageCropService,
            ITemplateManagementService templateService)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _imageCropService = imageCropService ?? throw new ArgumentNullException(nameof(imageCropService));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
        }

        public async Task<TemplateSelectionResult> SelectAndCropTemplateAsync(string templateName, Window owner)
        {
            try
            {
                // Step 1: Open file dialog to select source image
                var sourceImagePath = await SelectImageFileAsync(owner);
                if (string.IsNullOrWhiteSpace(sourceImagePath))
                {
                    return new TemplateSelectionResult { Success = false, ErrorMessage = "No image selected" };
                }

                // Step 2: Open ImageCropperDialog
                var cropResult = await OpenImageCropperAsync(sourceImagePath, owner);
                if (!cropResult.Success || cropResult.CropRectangle == null)
                {
                    return new TemplateSelectionResult { Success = false, ErrorMessage = "Crop cancelled" };
                }

                // Step 3: Create template file from cropped image
                var templatePath = await CreateTemplateFileAsync(templateName, sourceImagePath, cropResult.CropRectangle.Value);
                if (string.IsNullOrWhiteSpace(templatePath))
                {
                    return new TemplateSelectionResult { Success = false, ErrorMessage = "Failed to create template file" };
                }

                await _loggingService.LogInfoAsync($"Template created successfully: {templateName}");
                return new TemplateSelectionResult { Success = true, TemplatePath = templateName };
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error in SelectAndCropTemplateAsync: {ex.Message}", ex);
                return new TemplateSelectionResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<TemplateSelectionResult> ReplaceTemplateAsync(string existingTemplatePath, Window owner)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(existingTemplatePath))
                {
                    return new TemplateSelectionResult { Success = false, ErrorMessage = "No existing template path provided" };
                }

                // Step 1: Open file dialog to select source image
                var sourceImagePath = await SelectImageFileAsync(owner);
                if (string.IsNullOrWhiteSpace(sourceImagePath))
                {
                    return new TemplateSelectionResult { Success = false, ErrorMessage = "No image selected" };
                }

                // Step 2: Open ImageCropperDialog
                var cropResult = await OpenImageCropperAsync(sourceImagePath, owner);
                if (!cropResult.Success || cropResult.CropRectangle == null)
                {
                    return new TemplateSelectionResult { Success = false, ErrorMessage = "Crop cancelled" };
                }

                // Step 3: Replace existing template file
                var success = await _templateService.CropAndReplaceTemplateImageAsync(
                    existingTemplatePath,
                    sourceImagePath,
                    cropResult.CropRectangle.Value);

                if (!success)
                {
                    return new TemplateSelectionResult { Success = false, ErrorMessage = "Failed to replace template file" };
                }

                // Invalidate cache for the replaced template
                _templateService.Invalidate(existingTemplatePath);

                await _loggingService.LogInfoAsync($"Template replaced successfully: {existingTemplatePath}");
                return new TemplateSelectionResult { Success = true, TemplatePath = existingTemplatePath };
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error in ReplaceTemplateAsync: {ex.Message}", ex);
                return new TemplateSelectionResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<TemplateViewerResult> OpenTemplateViewerAsync(
            string templatePath,
            List<RelativeClickOffset>? existingClickPoints,
            bool existingFromCenterMode,
            Window owner)
        {
            try
            {
                if (!ValidateTemplatePath(templatePath))
                {
                    return new TemplateViewerResult { Success = false, ErrorMessage = "Template file not found" };
                }

                // Get TemplateViewerDialogViewModel and dialog from DI
                var vm = _serviceProvider.GetService(typeof(TemplateViewerDialogViewModel)) as TemplateViewerDialogViewModel;
                var dialog = _serviceProvider.GetService(typeof(TemplateViewerDialog)) as TemplateViewerDialog;

                if (vm == null || dialog == null)
                {
                    return new TemplateViewerResult { Success = false, ErrorMessage = "Template viewer not available from DI" };
                }

                // Configure viewer for click point selection
                vm.IsPickMode = true;
                vm.IsMultiPickMode = true;
                vm.FromCenterMode = existingFromCenterMode;
                vm.ResetMultiPick();

                // Load template image
                var templateFileName = templatePath.EndsWith(".png") ? templatePath : $"{templatePath}.png";
                await vm.LoadTemplateAsync(templateFileName, null);
                vm.ClickMarkers.Clear();

                // Load existing click points
                if (existingClickPoints != null)
                {
                    foreach (var point in existingClickPoints)
                    {
                        // Convert relative coordinates to pixel positions
                        double pixelX, pixelY;
                        if (point.FromCenter)
                        {
                            pixelX = (point.X + 0.5) * vm.TemplateImageWidth;
                            pixelY = (point.Y + 0.5) * vm.TemplateImageHeight;
                        }
                        else
                        {
                            pixelX = point.X * vm.TemplateImageWidth;
                            pixelY = point.Y * vm.TemplateImageHeight;
                        }

                        vm.ClickMarkers.Add(new Models.ClickMarker
                        {
                            X = pixelX,
                            Y = pixelY,
                            RelativeX = point.X,
                            RelativeY = point.Y,
                            Label = (vm.ClickMarkers.Count + 1).ToString(),
                            Description = point.Description ?? $"Click {vm.ClickMarkers.Count + 1}",
                            MarkerColor = System.Windows.Media.Brushes.DodgerBlue,
                            StepIndex = vm.ClickMarkers.Count
                        });
                    }
                }

                // Show dialog
                dialog.Owner = owner;
                dialog.DataContext = vm;
                dialog.Title = "Configure Click Points";

                var result = dialog.ShowDialog();

                if (result == true)
                {
                    // Convert markers back to RelativeClickOffset list
                    var clickPoints = new List<RelativeClickOffset>();
                    foreach (var marker in vm.ClickMarkers)
                    {
                        double nx, ny;
                        if (vm.FromCenterMode)
                        {
                            nx = (marker.X / vm.TemplateImageWidth) - 0.5;
                            ny = (marker.Y / vm.TemplateImageHeight) - 0.5;
                        }
                        else
                        {
                            nx = marker.X / vm.TemplateImageWidth;
                            ny = marker.Y / vm.TemplateImageHeight;
                        }

                        clickPoints.Add(new RelativeClickOffset
                        {
                            X = nx,
                            Y = ny,
                            Description = marker.Description,
                            FromCenter = vm.FromCenterMode
                        });
                    }

                    return new TemplateViewerResult
                    {
                        Success = true,
                        ClickPoints = clickPoints,
                        FromCenterMode = vm.FromCenterMode
                    };
                }

                return new TemplateViewerResult { Success = false, ErrorMessage = "Cancelled" };
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error in OpenTemplateViewerAsync: {ex.Message}", ex);
                return new TemplateViewerResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public bool ValidateTemplatePath(string? templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
                return false;

            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFileName = templatePath.EndsWith(".png") ? templatePath : $"{templatePath}.png";
                var filePath = Path.Combine(templatesPath, templateFileName);

                return File.Exists(filePath);
            }
            catch
            {
                return false;
            }
        }

        public async Task<BitmapImage?> LoadTemplateImageAsync(string? templatePath)
        {
            try
            {
                if (!ValidateTemplatePath(templatePath))
                    return null;

                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                var templateFileName = templatePath!.EndsWith(".png") ? templatePath : $"{templatePath}.png";
                var filePath = Path.Combine(templatesPath, templateFileName);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.DecodePixelHeight = 100; // Thumbnail size
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error loading template image: {ex.Message}", ex);
                return null;
            }
        }

        #region Private Helper Methods

        private Task<string?> SelectImageFileAsync(Window owner)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                string? result = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var openFileDialog = new OpenFileDialog
                    {
                        Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*",
                        Title = "Select Template Image"
                    };

                    if (openFileDialog.ShowDialog(owner) == true)
                    {
                        result = openFileDialog.FileName;
                    }
                });
                return result;
            });
        }

        private async Task<(bool Success, Rectangle? CropRectangle)> OpenImageCropperAsync(string sourceImagePath, Window owner)
        {
            try
            {
                // Create ImageCropperDialogViewModel
                var cropViewModel = new ImageCropperDialogViewModel(sourceImagePath, _loggingService);

                // Create and show ImageCropperDialog
                var cropDialog = new ImageCropperDialog(cropViewModel)
                {
                    Owner = owner
                };

                var dialogResult = cropDialog.ShowDialog();

                if (dialogResult == true && cropViewModel.CropRectangle.HasValue)
                {
                    return (true, cropViewModel.CropRectangle.Value);
                }

                return (false, null);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error opening image cropper: {ex.Message}", ex);
                return (false, null);
            }
        }

        private async Task<string?> CreateTemplateFileAsync(string templateName, string sourceImagePath, Rectangle cropRectangle)
        {
            try
            {
                // Get template directory path
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var templatesPath = Path.Combine(appDataPath, "FFXIManager", "workflows", "templates");
                Directory.CreateDirectory(templatesPath);

                var templateFilePath = Path.Combine(templatesPath, $"{templateName}.png");

                await _loggingService.LogInfoAsync($"Creating template file: {templateFilePath}");

                // Crop and save the image
                var success = await _imageCropService.CropImageAsync(sourceImagePath, cropRectangle, templateFilePath);

                if (success)
                {
                    // Invalidate any existing cache for this template
                    _templateService.Invalidate(templateName);

                    await _loggingService.LogInfoAsync($"Template file created: {templateFilePath}");
                    return templateName;
                }

                return null;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error creating template file: {ex.Message}", ex);
                return null;
            }
        }

        #endregion
    }
}

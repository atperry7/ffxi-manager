using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.ViewModels.Base;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// ViewModel for the Template Viewer Dialog.
    /// Displays a template image with visual markers showing where click actions will occur.
    /// </summary>
    public class TemplateViewerDialogViewModel : ViewModelBase
    {
        private readonly ILoggingService _loggingService;
        private readonly ITemplateManagementService _templateService;

        private BitmapSource? _templateImage;
        private double _templateImageWidth;
        private double _templateImageHeight;
        private string _templateName = string.Empty;
        private string _status = string.Empty;
        private bool _isPickMode;
        private double _pickedX;
        private double _pickedY;

        public TemplateViewerDialogViewModel(
            ILoggingService loggingService,
            ITemplateManagementService templateService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
        }

        /// <summary>
        /// The template image to display
        /// </summary>
        public BitmapSource? TemplateImage
        {
            get => _templateImage;
            set => SetProperty(ref _templateImage, value);
        }

        /// <summary>
        /// Width of the template image in pixels
        /// </summary>
        public double TemplateImageWidth
        {
            get => _templateImageWidth;
            set => SetProperty(ref _templateImageWidth, value);
        }

        /// <summary>
        /// Height of the template image in pixels
        /// </summary>
        public double TemplateImageHeight
        {
            get => _templateImageHeight;
            set => SetProperty(ref _templateImageHeight, value);
        }

        /// <summary>
        /// Name of the template
        /// </summary>
        public string TemplateName
        {
            get => _templateName;
            set => SetProperty(ref _templateName, value);
        }

        /// <summary>
        /// Status message for the dialog
        /// </summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>
        /// When true, the dialog accepts a click on the image to select a position.
        /// Coordinates are exposed via PickedX/PickedY (0.0 - 1.0) after dialog closes.
        /// </summary>
        public bool IsPickMode
        {
            get => _isPickMode;
            set => SetProperty(ref _isPickMode, value);
        }

        /// <summary>
        /// Picked X coordinate in relative units (0.0 - 1.0)
        /// </summary>
        public double PickedX
        {
            get => _pickedX;
            set => SetProperty(ref _pickedX, value);
        }

        /// <summary>
        /// Picked Y coordinate in relative units (0.0 - 1.0)
        /// </summary>
        public double PickedY
        {
            get => _pickedY;
            set => SetProperty(ref _pickedY, value);
        }

        /// <summary>
        /// Collection of click markers to display on the template
        /// </summary>
        public ObservableCollection<ClickMarker> ClickMarkers { get; } = new();

        /// <summary>
        /// Raised whenever a pick is made in pick mode (normalized X,Y)
        /// </summary>
        public event Action<double, double>? PickChanged;

        public void NotifyPickChanged()
        {
            PickChanged?.Invoke(PickedX, PickedY);
        }

        /// <summary>
        /// Loads and displays a template with click markers based on the navigation sequence
        /// </summary>
        /// <param name="templatePath">Path to the template file</param>
        /// <param name="navigationSequence">Navigation sequence containing click actions</param>
        public async Task LoadTemplateAsync(string templatePath, ObservableCollection<KeyboardAction>? navigationSequence)
        {
            try
            {
                Status = "Loading template...";

                // Load the template
                var template = await _templateService.LoadTemplateAsync(templatePath);
                if (template == null || template.ImageData == null || template.ImageData.Length == 0)
                {
                    Status = "Failed to load template image";
                    return;
                }

                // Convert BGR byte array to WPF BitmapSource
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
                TemplateName = template.Name;

                // Update click markers based on the navigation sequence
                UpdateClickMarkers(navigationSequence);

                Status = $"Loaded: {template.Width} × {template.Height} pixels";
                await _loggingService.LogDebugAsync($"Template viewer loaded: {templatePath} ({template.Width}x{template.Height})");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to load template for viewer: {ex.Message}", ex);
                Status = $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Updates the click markers collection based on the navigation sequence.
        /// Converts relative coordinates (0.0-1.0) to absolute pixel positions.
        /// </summary>
        private void UpdateClickMarkers(ObservableCollection<KeyboardAction>? navigationSequence)
        {
            ClickMarkers.Clear();

            if (navigationSequence == null || TemplateImageWidth == 0 || TemplateImageHeight == 0)
                return;

            int stepIndex = 1;

            // Add markers for each Click action in the sequence
            foreach (var step in navigationSequence)
            {
                if (step.Action?.Equals("Click", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Convert relative coordinates (0.0-1.0) to absolute pixel positions
                    var pixelX = step.ClickX * TemplateImageWidth;
                    var pixelY = step.ClickY * TemplateImageHeight;

                    ClickMarkers.Add(new ClickMarker
                    {
                        X = pixelX,
                        Y = pixelY,
                        Label = stepIndex.ToString(),
                        Description = step.Description ?? "Click action",
                        MarkerColor = Brushes.DodgerBlue,
                        StepIndex = stepIndex - 1
                    });
                }
                stepIndex++;
            }

            _loggingService.LogDebugAsync($"Template viewer: Updated {ClickMarkers.Count} click markers");
        }
    }
}

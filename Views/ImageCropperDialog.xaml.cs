using System.Windows;
using System.Windows.Input;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    /// <summary>
    /// Interaction logic for ImageCropperDialog.xaml
    /// </summary>
    public partial class ImageCropperDialog : Window
    {
        private ImageCropperDialogViewModel ViewModel => (ImageCropperDialogViewModel)DataContext;
        private EventHandler? _closeHandler;

        public ImageCropperDialog(ImageCropperDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Subscribe to close event with a stored reference for cleanup
            _closeHandler = (s, e) =>
            {
                DialogResult = viewModel.DialogResult;
                Close();
            };
            viewModel.RequestClose += _closeHandler;

            // Ensure cleanup on window closing
            Closing += ImageCropperDialog_Closing;
        }

        private void ImageCropperDialog_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Release mouse capture if still active
            if (ImageCanvas.IsMouseCaptured)
            {
                ImageCanvas.ReleaseMouseCapture();
            }

            // Unsubscribe from events to prevent memory leaks
            if (ViewModel != null && _closeHandler != null)
            {
                ViewModel.RequestClose -= _closeHandler;
                _closeHandler = null;
            }

            Closing -= ImageCropperDialog_Closing;

            // Clear DataContext to release ViewModel reference
            DataContext = null;
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Get mouse position relative to the Viewbox (the container)
            var viewboxPosition = e.GetPosition(ImageViewbox);

            // Get Viewbox actual (rendered) size
            double viewboxWidth = ImageViewbox.ActualWidth;
            double viewboxHeight = ImageViewbox.ActualHeight;

            // Get image logical size
            double imageWidth = ViewModel.ImageWidth;
            double imageHeight = ViewModel.ImageHeight;

            // Calculate uniform scale (Viewbox uses Stretch="Uniform")
            double scaleX = viewboxWidth / imageWidth;
            double scaleY = viewboxHeight / imageHeight;
            double scale = Math.Min(scaleX, scaleY); // Uniform scaling uses minimum

            // Calculate actual rendered image size after scaling
            double renderedWidth = imageWidth * scale;
            double renderedHeight = imageHeight * scale;

            // Calculate centering offset (Viewbox centers the content)
            double offsetX = (viewboxWidth - renderedWidth) / 2.0;
            double offsetY = (viewboxHeight - renderedHeight) / 2.0;

            // Transform from Viewbox coordinates to image coordinates
            double imageX = (viewboxPosition.X - offsetX) / scale;
            double imageY = (viewboxPosition.Y - offsetY) / scale;

            System.Diagnostics.Debug.WriteLine($"[CROP] ===== Mouse Down =====");
            System.Diagnostics.Debug.WriteLine($"[CROP] Viewbox size: {viewboxWidth:F2} x {viewboxHeight:F2}");
            System.Diagnostics.Debug.WriteLine($"[CROP] Image size: {imageWidth} x {imageHeight}");
            System.Diagnostics.Debug.WriteLine($"[CROP] Scale: {scale:F6}");
            System.Diagnostics.Debug.WriteLine($"[CROP] Rendered size: {renderedWidth:F2} x {renderedHeight:F2}");
            System.Diagnostics.Debug.WriteLine($"[CROP] Offset: ({offsetX:F2}, {offsetY:F2})");
            System.Diagnostics.Debug.WriteLine($"[CROP] Viewbox pos: ({viewboxPosition.X:F2}, {viewboxPosition.Y:F2})");
            System.Diagnostics.Debug.WriteLine($"[CROP] Image pos: ({imageX:F2}, {imageY:F2})");

            ViewModel.StartSelection(imageX, imageY);
            ImageCanvas.CaptureMouse();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!ViewModel.IsSelecting)
                return;

            // Same transformation logic as MouseDown
            var viewboxPosition = e.GetPosition(ImageViewbox);
            double viewboxWidth = ImageViewbox.ActualWidth;
            double viewboxHeight = ImageViewbox.ActualHeight;
            double imageWidth = ViewModel.ImageWidth;
            double imageHeight = ViewModel.ImageHeight;

            double scaleX = viewboxWidth / imageWidth;
            double scaleY = viewboxHeight / imageHeight;
            double scale = Math.Min(scaleX, scaleY);

            double renderedWidth = imageWidth * scale;
            double renderedHeight = imageHeight * scale;
            double offsetX = (viewboxWidth - renderedWidth) / 2.0;
            double offsetY = (viewboxHeight - renderedHeight) / 2.0;

            double imageX = (viewboxPosition.X - offsetX) / scale;
            double imageY = (viewboxPosition.Y - offsetY) / scale;

            ViewModel.UpdateSelection(imageX, imageY);
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel.IsSelecting)
            {
                ViewModel.EndSelection();
                ImageCanvas.ReleaseMouseCapture();
            }
        }
    }
}

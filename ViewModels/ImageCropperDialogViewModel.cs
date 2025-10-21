using FFXIManager.Services;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// ViewModel for the image cropper dialog that allows users to select a region from an image
    /// </summary>
    public class ImageCropperDialogViewModel : INotifyPropertyChanged
    {
        private readonly ILoggingService _loggingService;
        private readonly string _sourceImagePath;

        private BitmapSource? _sourceImage;
        private double _imageWidth;
        private double _imageHeight;
        private bool _isSelecting;
        private double _selectionStartX;
        private double _selectionStartY;
        private double _selectionX;
        private double _selectionY;
        private double _selectionWidth;
        private double _selectionHeight;
        private bool _hasSelection;

        public BitmapSource? SourceImage
        {
            get => _sourceImage;
            private set { _sourceImage = value; OnPropertyChanged(); }
        }

        public double ImageWidth
        {
            get => _imageWidth;
            private set { _imageWidth = value; OnPropertyChanged(); }
        }

        public double ImageHeight
        {
            get => _imageHeight;
            private set { _imageHeight = value; OnPropertyChanged(); }
        }

        public bool IsSelecting
        {
            get => _isSelecting;
            set { _isSelecting = value; OnPropertyChanged(); }
        }

        public double SelectionX
        {
            get => _selectionX;
            set { _selectionX = value; OnPropertyChanged(); }
        }

        public double SelectionY
        {
            get => _selectionY;
            set { _selectionY = value; OnPropertyChanged(); }
        }

        public double SelectionWidth
        {
            get => _selectionWidth;
            set { _selectionWidth = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectionInfo)); }
        }

        public double SelectionHeight
        {
            get => _selectionHeight;
            set { _selectionHeight = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectionInfo)); }
        }

        public bool HasSelection
        {
            get => _hasSelection;
            set { _hasSelection = value; OnPropertyChanged(); ((RelayCommand)ApplyCropCommand).RaiseCanExecuteChanged(); }
        }

        public string SelectionInfo
        {
            get
            {
                if (!HasSelection)
                    return "Drag to select a region, or click 'Use Full Image'";

                return $"Selected: {SelectionWidth:F0} × {SelectionHeight:F0} pixels | Position: ({SelectionX:F0}, {SelectionY:F0})";
            }
        }

        public ICommand ApplyCropCommand { get; }
        public ICommand UseFullImageCommand { get; }
        public ICommand CancelCommand { get; }

        /// <summary>
        /// The crop rectangle result (null if full image should be used)
        /// </summary>
        public Rectangle? CropRectangle { get; private set; }

        /// <summary>
        /// Dialog result (true if user confirmed, false if cancelled)
        /// </summary>
        public bool? DialogResult { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? RequestClose;

        public ImageCropperDialogViewModel(string sourceImagePath, ILoggingService loggingService)
        {
            _sourceImagePath = sourceImagePath ?? throw new ArgumentNullException(nameof(sourceImagePath));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            ApplyCropCommand = new RelayCommand(ApplyCrop, () => HasSelection);
            UseFullImageCommand = new RelayCommand(UseFullImage);
            CancelCommand = new RelayCommand(Cancel);

            _ = LoadImageAsync();
        }

        private async System.Threading.Tasks.Task LoadImageAsync()
        {
            try
            {
                if (!File.Exists(_sourceImagePath))
                {
                    await _loggingService.LogErrorAsync($"Source image not found: {_sourceImagePath}");
                    return;
                }

                // Load image and force it to use actual pixel dimensions by ignoring DPI
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(_sourceImagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                // CreateOptions.IgnoreColorProfile can help with consistency
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                bitmap.Freeze();

                SourceImage = bitmap;

                // Use actual pixel dimensions - these should match System.Drawing.Bitmap dimensions
                ImageWidth = bitmap.PixelWidth;
                ImageHeight = bitmap.PixelHeight;

                // Log dimensions and DPI for debugging
                await _loggingService.LogInfoAsync(
                    $"WPF BitmapImage loaded: {ImageWidth}x{ImageHeight} pixels, DPI: {bitmap.DpiX}x{bitmap.DpiY}");

                // Also check System.Drawing dimensions to verify they match
                using (var gdiBitmap = new System.Drawing.Bitmap(_sourceImagePath))
                {
                    await _loggingService.LogInfoAsync(
                        $"GDI+ Bitmap dimensions: {gdiBitmap.Width}x{gdiBitmap.Height}, " +
                        $"HorizontalResolution: {gdiBitmap.HorizontalResolution}, VerticalResolution: {gdiBitmap.VerticalResolution}");

                    if (gdiBitmap.Width != ImageWidth || gdiBitmap.Height != ImageHeight)
                    {
                        await _loggingService.LogWarningAsync(
                            $"DIMENSION MISMATCH! WPF reports {ImageWidth}x{ImageHeight} but GDI+ reports {gdiBitmap.Width}x{gdiBitmap.Height}. " +
                            $"This will cause coordinate misalignment during cropping!");
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to load image: {ex.Message}", ex);
            }
        }

        public void StartSelection(double x, double y)
        {
            _ = _loggingService.LogInfoAsync($"[CROP DEBUG] StartSelection - Raw input: ({x:F2}, {y:F2}), Image size: {ImageWidth}x{ImageHeight}");

            IsSelecting = true;
            _selectionStartX = Math.Clamp(x, 0, ImageWidth);
            _selectionStartY = Math.Clamp(y, 0, ImageHeight);
            SelectionX = _selectionStartX;
            SelectionY = _selectionStartY;
            SelectionWidth = 0;
            SelectionHeight = 0;
            HasSelection = false;

            _ = _loggingService.LogInfoAsync($"[CROP DEBUG] Selection started at: ({_selectionStartX:F2}, {_selectionStartY:F2})");
        }

        public void UpdateSelection(double x, double y)
        {
            if (!IsSelecting)
                return;

            x = Math.Clamp(x, 0, ImageWidth);
            y = Math.Clamp(y, 0, ImageHeight);

            var minX = Math.Min(_selectionStartX, x);
            var minY = Math.Min(_selectionStartY, y);
            var maxX = Math.Max(_selectionStartX, x);
            var maxY = Math.Max(_selectionStartY, y);

            SelectionX = minX;
            SelectionY = minY;
            SelectionWidth = maxX - minX;
            SelectionHeight = maxY - minY;

            // Show selection rectangle while dragging (not just after release)
            if (SelectionWidth >= 1 && SelectionHeight >= 1)
            {
                HasSelection = true;
            }
        }

        public void EndSelection()
        {
            IsSelecting = false;

            _ = _loggingService.LogInfoAsync(
                $"[CROP DEBUG] EndSelection - Rectangle: ({SelectionX:F2}, {SelectionY:F2}) {SelectionWidth:F2}x{SelectionHeight:F2}");

            // Only set HasSelection if the selection is large enough
            if (SelectionWidth >= 10 && SelectionHeight >= 10)
            {
                HasSelection = true;
                _ = _loggingService.LogInfoAsync($"[CROP DEBUG] Selection accepted (size >= 10x10)");
            }
            else
            {
                // Reset selection if too small
                SelectionX = 0;
                SelectionY = 0;
                SelectionWidth = 0;
                SelectionHeight = 0;
                HasSelection = false;
                _ = _loggingService.LogInfoAsync($"[CROP DEBUG] Selection rejected (too small)");
            }
        }

        private async void ApplyCrop()
        {
            if (!HasSelection)
                return;

            CropRectangle = new Rectangle(
                (int)SelectionX,
                (int)SelectionY,
                (int)SelectionWidth,
                (int)SelectionHeight
            );

            await _loggingService.LogInfoAsync(
                $"Crop rectangle created: X={CropRectangle.Value.X}, Y={CropRectangle.Value.Y}, " +
                $"Width={CropRectangle.Value.Width}, Height={CropRectangle.Value.Height}");

            DialogResult = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private async void UseFullImage()
        {
            // Create a rectangle representing the full image dimensions
            CropRectangle = new Rectangle(0, 0, (int)ImageWidth, (int)ImageHeight);

            await _loggingService.LogInfoAsync(
                $"Using full image: X=0, Y=0, Width={ImageWidth}, Height={ImageHeight}");

            DialogResult = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel()
        {
            CropRectangle = null;
            DialogResult = false;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

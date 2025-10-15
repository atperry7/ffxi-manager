using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service implementation for image cropping operations
    /// </summary>
    public class ImageCropService : IImageCropService
    {
        private readonly ILoggingService _loggingService;

        public ImageCropService(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public async Task<bool> CropImageAsync(string sourcePath, Rectangle cropRectangle, string outputPath)
        {
            try
            {
                if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(outputPath))
                {
                    await _loggingService.LogWarningAsync("Invalid paths provided for image cropping");
                    return false;
                }

                if (!File.Exists(sourcePath))
                {
                    await _loggingService.LogWarningAsync($"Source image not found: {sourcePath}");
                    return false;
                }

                // Validate crop rectangle
                if (!await ValidateCropRectangleAsync(sourcePath, cropRectangle))
                {
                    await _loggingService.LogWarningAsync($"Invalid crop rectangle: {cropRectangle}");
                    return false;
                }

                await _loggingService.LogInfoAsync($"Cropping image: {sourcePath} to {cropRectangle}");

                using var sourceImage = new Bitmap(sourcePath);
                using var croppedImage = new Bitmap(cropRectangle.Width, cropRectangle.Height, sourceImage.PixelFormat);
                using var graphics = Graphics.FromImage(croppedImage);

                // Use high quality settings for cropping
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                // Draw the cropped region
                graphics.DrawImage(
                    sourceImage,
                    new Rectangle(0, 0, cropRectangle.Width, cropRectangle.Height),
                    cropRectangle,
                    GraphicsUnit.Pixel
                );

                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Save as PNG
                croppedImage.Save(outputPath, ImageFormat.Png);

                await _loggingService.LogInfoAsync($"Successfully cropped image to: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to crop image: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> ValidateCropRectangleAsync(string imagePath, Rectangle cropRectangle)
        {
            try
            {
                if (!File.Exists(imagePath))
                    return false;

                var dimensions = await GetImageDimensionsAsync(imagePath);
                if (dimensions == null)
                    return false;

                // Validate crop rectangle is within image bounds
                if (cropRectangle.X < 0 || cropRectangle.Y < 0)
                    return false;

                if (cropRectangle.Width <= 0 || cropRectangle.Height <= 0)
                    return false;

                if (cropRectangle.Right > dimensions.Value.Width || cropRectangle.Bottom > dimensions.Value.Height)
                    return false;

                // Validate minimum dimensions
                if (cropRectangle.Width < 10 || cropRectangle.Height < 10)
                {
                    await _loggingService.LogWarningAsync($"Crop rectangle too small: {cropRectangle.Width}x{cropRectangle.Height}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to validate crop rectangle: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<Size?> GetImageDimensionsAsync(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                    return null;

                using var image = new Bitmap(imagePath);
                return new Size(image.Width, image.Height);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to get image dimensions: {ex.Message}", ex);
                return null;
            }
        }
    }
}

using System.Drawing;
using System.Threading.Tasks;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for cropping images to specific regions
    /// </summary>
    public interface IImageCropService
    {
        /// <summary>
        /// Crops an image to the specified rectangle region
        /// </summary>
        /// <param name="sourcePath">Full path to the source image file</param>
        /// <param name="cropRectangle">Rectangle defining the crop region (X, Y, Width, Height)</param>
        /// <param name="outputPath">Full path where the cropped image will be saved</param>
        /// <returns>True if cropping succeeded, false otherwise</returns>
        Task<bool> CropImageAsync(string sourcePath, Rectangle cropRectangle, string outputPath);

        /// <summary>
        /// Validates that a crop rectangle is valid for the given image
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <param name="cropRectangle">Rectangle to validate</param>
        /// <returns>True if the crop rectangle is valid for the image</returns>
        Task<bool> ValidateCropRectangleAsync(string imagePath, Rectangle cropRectangle);

        /// <summary>
        /// Gets the dimensions of an image file
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <returns>Size of the image (Width, Height), or null if image cannot be loaded</returns>
        Task<Size?> GetImageDimensionsAsync(string imagePath);
    }
}

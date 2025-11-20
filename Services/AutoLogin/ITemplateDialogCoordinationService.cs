using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using System.Windows.Media.Imaging;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Result of template selection and cropping operation
    /// </summary>
    public record TemplateSelectionResult
    {
        public bool Success { get; init; }
        public string? TemplatePath { get; init; }
        public string? ErrorMessage { get; init; }
    }

    /// <summary>
    /// Result of template viewer operation for click point configuration
    /// </summary>
    public record TemplateViewerResult
    {
        public bool Success { get; init; }
        public List<RelativeClickOffset>? ClickPoints { get; init; }
        public bool FromCenterMode { get; init; }
        public string? ErrorMessage { get; init; }
    }

    /// <summary>
    /// Coordinates dialog interactions for template selection, cropping, and click point configuration.
    /// Extracts dialog lifecycle management from ViewModels for better separation of concerns.
    /// </summary>
    public interface ITemplateDialogCoordinationService
    {
        /// <summary>
        /// Opens file dialog to select an image, then opens ImageCropperDialog for cropping.
        /// Creates the template file in the workflow templates directory.
        /// </summary>
        /// <param name="templateName">Name for the template file (without extension)</param>
        /// <param name="owner">Owner window for dialog modality</param>
        /// <returns>Result containing success status and template path</returns>
        Task<TemplateSelectionResult> SelectAndCropTemplateAsync(string templateName, System.Windows.Window owner);

        /// <summary>
        /// Opens ImageCropperDialog to replace an existing template image.
        /// </summary>
        /// <param name="existingTemplatePath">Path to existing template to replace</param>
        /// <param name="owner">Owner window for dialog modality</param>
        /// <returns>Result containing success status and new template path</returns>
        Task<TemplateSelectionResult> ReplaceTemplateAsync(string existingTemplatePath, System.Windows.Window owner);

        /// <summary>
        /// Opens TemplateViewerDialog to configure click points on a template image.
        /// </summary>
        /// <param name="templatePath">Path to template image</param>
        /// <param name="existingClickPoints">Existing click points to show (if any)</param>
        /// <param name="existingFromCenterMode">Existing center-relative mode setting</param>
        /// <param name="owner">Owner window for dialog modality</param>
        /// <returns>Result containing success status and configured click points</returns>
        Task<TemplateViewerResult> OpenTemplateViewerAsync(
            string templatePath,
            List<RelativeClickOffset>? existingClickPoints,
            bool existingFromCenterMode,
            System.Windows.Window owner);

        /// <summary>
        /// Validates that a template file exists and is readable.
        /// </summary>
        /// <param name="templatePath">Path to template file</param>
        /// <returns>True if valid, false otherwise</returns>
        bool ValidateTemplatePath(string? templatePath);

        /// <summary>
        /// Loads a template image as a BitmapImage for display in UI.
        /// </summary>
        /// <param name="templatePath">Path to template file</param>
        /// <returns>BitmapImage if successful, null if file doesn't exist or can't be loaded</returns>
        Task<BitmapImage?> LoadTemplateImageAsync(string? templatePath);
    }
}

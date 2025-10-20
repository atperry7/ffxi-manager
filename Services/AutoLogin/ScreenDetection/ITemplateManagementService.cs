using System.Collections.Generic;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Service for managing UI element templates
    /// </summary>
    public interface ITemplateManagementService
    {
        /// <summary>
        /// Loads a template by its path
        /// </summary>
        /// <param name="templatePath">Path to the template PNG file (e.g., "launch_arrow.png")</param>
        /// <param name="confidenceThreshold">Confidence threshold for template matching (0.0 to 1.0)</param>
        /// <param name="tolerance">Position tolerance in pixels</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The loaded UI element template</returns>
        Task<UIElementTemplate?> LoadTemplateAsync(string templatePath, float confidenceThreshold = 0.8f, int tolerance = 5, CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads a template by its path with default metadata
        /// </summary>
        /// <param name="templatePath">Path to the template PNG file (e.g., "launch_arrow.png")</param>
        /// <returns>The loaded UI element template</returns>
        Task<UIElementTemplate?> LoadTemplateAsync(string templatePath);

        /// <summary>
        /// Loads all templates for a specific application
        /// </summary>
        /// <param name="applicationName">Name of the application (e.g., "Windower", "PlayOnline", "FFXI")</param>
        /// <returns>Collection of templates for the application</returns>
        Task<IList<UIElementTemplate>> LoadTemplatesForApplicationAsync(string applicationName);

        /// <summary>
        /// Preloads and caches all templates for performance
        /// </summary>
        /// <returns>Number of templates loaded</returns>
        Task<int> PreloadAllTemplatesAsync();

        /// <summary>
        /// Clears the template cache
        /// </summary>
        void ClearCache();

        /// <summary>
        /// Invalidates cached entries for a specific template path (all variants).
        /// </summary>
        /// <param name="templatePath">Template file name or path without extension</param>
        void Invalidate(string templatePath);


        /// <summary>
        /// Lists all available template paths
        /// </summary>
        /// <returns>Collection of all template paths</returns>
        IEnumerable<string> GetAvailableTemplatePaths();

        /// <summary>
        /// Asynchronously lists all available template paths (convenience for UI)
        /// </summary>
        /// <returns>Collection of all template paths</returns>
        Task<IList<string>> GetAvailableTemplatePathsAsync(System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates that a template exists and is valid
        /// </summary>
        /// <param name="templatePath">Path to the template</param>
        /// <returns>True if the template exists and is valid</returns>
        Task<bool> ValidateTemplateAsync(string templatePath);

        

        /// <summary>
        /// Replaces the PNG image file for an existing template.
        /// Backs up the original file and validates the new image before replacement.
        /// </summary>
        /// <param name="templatePath">Path to the template (e.g., "PlayOnline/login_information_screen")</param>
        /// <param name="newImagePath">Full path to the new image file</param>
        /// <returns>True if replacement succeeded, false otherwise</returns>
        Task<bool> ReplaceTemplateImageAsync(string templatePath, string newImagePath);

        /// <summary>
        /// Crops and replaces the PNG image file for an existing template.
        /// Allows selecting a specific region from a larger screenshot.
        /// Backs up the original file and validates the new image before replacement.
        /// </summary>
        /// <param name="templatePath">Path to the template (e.g., "PlayOnline/login_information_screen")</param>
        /// <param name="newImagePath">Full path to the new image file</param>
        /// <param name="cropRectangle">Optional rectangle to crop from the source image. If null, uses full image.</param>
        /// <returns>True if replacement succeeded, false otherwise</returns>
        Task<bool> CropAndReplaceTemplateImageAsync(string templatePath, string newImagePath, System.Drawing.Rectangle? cropRectangle);
    }

    
}

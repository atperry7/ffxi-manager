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
        /// <param name="templatePath">Path to the template (e.g., "Windower/launch_arrow")</param>
        /// <returns>The loaded UI element template</returns>
        Task<UIElementTemplate?> LoadTemplateAsync(string templatePath);

        /// <summary>
        /// Loads all templates for a specific login task step
        /// </summary>
        /// <param name="step">The login task step</param>
        /// <returns>Collection of templates for the step</returns>
        Task<IList<UIElementTemplate>> LoadTemplatesForStepAsync(LoginTaskStep step);

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
        /// Gets template metadata without loading the image data
        /// </summary>
        /// <param name="templatePath">Path to the template</param>
        /// <returns>Template metadata</returns>
        Task<TemplateMetadata?> GetTemplateMetadataAsync(string templatePath);

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
        /// Gets the version of a template
        /// </summary>
        /// <param name="templatePath">Path to the template</param>
        /// <returns>Template version string</returns>
        Task<string> GetTemplateVersionAsync(string templatePath);

        /// <summary>
        /// Updates only the navigation section of a template's JSON metadata and refreshes caches.
        /// </summary>
        /// <param name="templatePath">Path to the template (e.g., "PlayOnline/login_information_screen")</param>
        /// <param name="navigation">Navigation action to persist</param>
        /// <returns>True if the update succeeded</returns>
        Task<bool> UpdateTemplateNavigationAsync(string templatePath, NavigationAction navigation);

        /// <summary>
        /// Validates all templates on disk for Hybrid conformance and deprecated fields.
        /// Logs warnings for any issues and returns a summary report.
        /// </summary>
        Task<TemplateValidationReport> ValidateAllTemplatesAsync(System.Threading.CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Summary report for template validation diagnostics.
    /// </summary>
    public class TemplateValidationReport
    {
        public int TotalTemplates { get; set; }
        public int HybridConformant { get; set; }
        public int NonHybrid { get; set; }
        public int DeprecatedActionOffsets { get; set; }
        public int DeprecatedAbsoluteBlocks { get; set; }
    }
}

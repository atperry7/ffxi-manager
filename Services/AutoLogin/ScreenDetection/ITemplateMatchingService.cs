using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Service for matching UI element templates within screenshots
    /// </summary>
    public interface ITemplateMatchingService
    {
        /// <summary>
        /// Finds a UI element template within a window screenshot
        /// </summary>
        /// <param name="screenshot">The window screenshot to search in</param>
        /// <param name="template">The UI element template to find</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Match result with confidence and position</returns>
        Task<TemplateMatchResult> FindElementAsync(
            WindowScreenshot screenshot,
            UIElementTemplate template,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds a UI element by template path
        /// </summary>
        /// <param name="screenshot">The window screenshot to search in</param>
        /// <param name="templatePath">Path to the template (e.g., "Windower/launch_arrow")</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Match result with confidence and position</returns>
        Task<TemplateMatchResult> FindElementAsync(
            WindowScreenshot screenshot,
            string templatePath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds multiple UI elements in a screenshot
        /// </summary>
        /// <param name="screenshot">The window screenshot to search in</param>
        /// <param name="templates">Collection of templates to find</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Collection of match results</returns>
        Task<IList<TemplateMatchResult>> FindMultipleElementsAsync(
            WindowScreenshot screenshot,
            IEnumerable<UIElementTemplate> templates,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds all instances of a template in a screenshot
        /// </summary>
        /// <param name="screenshot">The window screenshot to search in</param>
        /// <param name="template">The template to find</param>
        /// <param name="maxMatches">Maximum number of matches to return</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>All matches found above the confidence threshold</returns>
        Task<IList<TemplateMatchResult>> FindAllInstancesAsync(
            WindowScreenshot screenshot,
            UIElementTemplate template,
            int maxMatches = 10,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates if a template match is still valid (for verification after actions)
        /// </summary>
        /// <param name="screenshot">Current window screenshot</param>
        /// <param name="previousMatch">Previous match result to validate</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if the element is still present at the expected location</returns>
        Task<bool> ValidateMatchAsync(
            WindowScreenshot screenshot,
            TemplateMatchResult previousMatch,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the default confidence threshold for matching
        /// </summary>
        /// <param name="threshold">Confidence threshold (0.0 to 1.0)</param>
        void SetDefaultConfidenceThreshold(float threshold);

        /// <summary>
        /// Gets the current default confidence threshold
        /// </summary>
        /// <returns>Current confidence threshold</returns>
        float GetDefaultConfidenceThreshold();
    }
}
using System;
using System.Collections.Generic;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Represents the detected state of an application screen
    /// </summary>
    public class ScreenState
    {
        /// <summary>
        /// The login task step this state represents
        /// </summary>
        public LoginTaskStep DetectedStep { get; set; } = LoginTaskStep.None;

        /// <summary>
        /// Confidence level of the detection (0.0 to 1.0)
        /// </summary>
        public float Confidence { get; set; }

        /// <summary>
        /// List of UI elements detected on the screen
        /// </summary>
        public List<TemplateMatchResult> DetectedElements { get; set; } = new();

        /// <summary>
        /// Timestamp when the state was detected
        /// </summary>
        public DateTime DetectionTime { get; set; } = DateTime.Now;

        /// <summary>
        /// Application name (e.g., "Windower", "PlayOnline", "FFXI")
        /// </summary>
        public string ApplicationName { get; set; } = string.Empty;

        /// <summary>
        /// Available actions for this state
        /// </summary>
        public List<UIAutomationAction> AvailableActions { get; set; } = new();

        /// <summary>
        /// Next expected states after actions are performed
        /// </summary>
        public List<LoginTaskStep> NextExpectedSteps { get; set; } = new();

        /// <summary>
        /// Whether this state is ready for automation
        /// </summary>
        public bool IsReady { get; set; } = true;

        /// <summary>
        /// Error message if state detection failed
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Additional metadata about the state
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        /// <summary>
        /// Whether the state detection was successful
        /// </summary>
        public bool IsValid => DetectedStep != LoginTaskStep.None && Confidence > 0;

        /// <summary>
        /// Gets the primary detected element (highest confidence)
        /// </summary>
        public TemplateMatchResult? GetPrimaryElement()
        {
            if (DetectedElements.Count == 0) return null;

            TemplateMatchResult? bestMatch = null;
            foreach (var element in DetectedElements)
            {
                if (bestMatch == null || element.Confidence > bestMatch.Confidence)
                {
                    bestMatch = element;
                }
            }
            return bestMatch;
        }

        /// <summary>
        /// Gets detected elements for a specific action type
        /// </summary>
        public List<TemplateMatchResult> GetElementsForAction(UIActionType actionType)
        {
            var results = new List<TemplateMatchResult>();
            foreach (var element in DetectedElements)
            {
                if (element.Template?.ActionType == actionType)
                {
                    results.Add(element);
                }
            }
            return results;
        }

        /// <summary>
        /// Checks if a specific UI element is present
        /// </summary>
        public bool HasElement(string templatePath)
        {
            foreach (var element in DetectedElements)
            {
                if (element.Template?.TemplatePath == templatePath && element.IsValid)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the default action for this state
        /// </summary>
        public UIAutomationAction? GetDefaultAction()
        {
            return AvailableActions.Count > 0 ? AvailableActions[0] : null;
        }

        /// <summary>
        /// Creates a failed state result
        /// </summary>
        public static ScreenState Failed(string errorMessage)
        {
            return new ScreenState
            {
                DetectedStep = LoginTaskStep.None,
                Confidence = 0.0f,
                IsReady = false,
                ErrorMessage = errorMessage,
                DetectionTime = DateTime.Now
            };
        }

        /// <summary>
        /// Creates a successful state result
        /// </summary>
        public static ScreenState Success(
            LoginTaskStep step,
            float confidence,
            List<TemplateMatchResult> elements,
            string applicationName)
        {
            return new ScreenState
            {
                DetectedStep = step,
                Confidence = confidence,
                DetectedElements = elements ?? new List<TemplateMatchResult>(),
                ApplicationName = applicationName,
                IsReady = true,
                DetectionTime = DateTime.Now
            };
        }

        /// <summary>
        /// Creates an unknown/transitioning state
        /// </summary>
        public static ScreenState Unknown()
        {
            return new ScreenState
            {
                DetectedStep = LoginTaskStep.None,
                Confidence = 0.0f,
                IsReady = false,
                ErrorMessage = "Screen state could not be determined",
                DetectionTime = DateTime.Now
            };
        }
    }
}
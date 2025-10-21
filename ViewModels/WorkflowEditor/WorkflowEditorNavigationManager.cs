using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Services;
using System.Collections.ObjectModel;

namespace FFXIManager.ViewModels.WorkflowEditor
{
    /// <summary>
    /// Manages navigation action operations for the Workflow Editor.
    /// Handles initialization, CRUD, and reordering of navigation sequences.
    /// </summary>
    public class WorkflowEditorNavigationManager
    {
        private readonly ILoggingService _loggingService;

        public WorkflowEditorNavigationManager(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        /// <summary>
        /// Initializes a new navigation sequence for the given step
        /// </summary>
        public void InitializeNavigation(WorkflowStepDefinition step)
        {
            if (step == null) return;

            try
            {
                step.Navigation = new NavigationAction
                {
                    Description = $"Navigation for {step.DisplayName}",
                    PostNavigationDelayMs = 500,
                    Sequence = new ObservableCollection<KeyboardAction>()
                };

                _ = _loggingService.LogDebugAsync($"Initialized sequence-based navigation for step: {step.DisplayName}");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error initializing navigation", ex);
            }
        }

        /// <summary>
        /// Adds a new navigation action to the step's navigation sequence
        /// </summary>
        /// <returns>The newly created action, or null if operation failed</returns>
        public KeyboardAction? AddNavigationAction(WorkflowStepDefinition step)
        {
            if (step?.Navigation == null) return null;

            try
            {
                var newAction = new KeyboardAction
                {
                    Action = "Tab",
                    Count = 1,
                    DelayMs = 100,
                    Description = "Navigate to next field"
                };

                step.Navigation.Sequence.Add(newAction);

                _ = _loggingService.LogDebugAsync($"Added navigation action to step: {step.DisplayName}");

                return newAction;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error adding navigation action", ex);
                return null;
            }
        }

        /// <summary>
        /// Removes the specified navigation action from the step's sequence
        /// </summary>
        public bool RemoveNavigationAction(WorkflowStepDefinition step, KeyboardAction actionToRemove)
        {
            if (step?.Navigation == null || actionToRemove == null) return false;

            try
            {
                var removed = step.Navigation.Sequence.Remove(actionToRemove);

                if (removed)
                {
                    _ = _loggingService.LogDebugAsync($"Removed navigation action from step: {step.DisplayName}");
                }

                return removed;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error removing navigation action", ex);
                return false;
            }
        }

        /// <summary>
        /// Moves the specified navigation action up in the sequence
        /// </summary>
        public bool MoveNavigationActionUp(WorkflowStepDefinition step, KeyboardAction action)
        {
            if (step?.Navigation == null || action == null) return false;

            try
            {
                var sequence = step.Navigation.Sequence;
                var index = sequence.IndexOf(action);

                if (index <= 0) return false;

                sequence.Move(index, index - 1);

                _ = _loggingService.LogDebugAsync($"Moved navigation action up in step: {step.DisplayName}");

                return true;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error moving navigation action up", ex);
                return false;
            }
        }

        /// <summary>
        /// Moves the specified navigation action down in the sequence
        /// </summary>
        public bool MoveNavigationActionDown(WorkflowStepDefinition step, KeyboardAction action)
        {
            if (step?.Navigation == null || action == null) return false;

            try
            {
                var sequence = step.Navigation.Sequence;
                var index = sequence.IndexOf(action);

                if (index < 0 || index >= sequence.Count - 1) return false;

                sequence.Move(index, index + 1);

                _ = _loggingService.LogDebugAsync($"Moved navigation action down in step: {step.DisplayName}");

                return true;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error moving navigation action down", ex);
                return false;
            }
        }
    }
}

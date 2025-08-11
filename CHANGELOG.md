# Changelog

All notable changes in this branch are documented here to prepare for merge.

## Unreleased

### Added
- Centralized process/window monitoring via ProcessManagementService with:
  - WinEvent hook for window title changes (EVENT_OBJECT_NAMECHANGE) to power real-time updates
  - WMI watchers for process start/stop with structured event propagation (ProcessDetected/Terminated/Updated)
  - Unified discovery watch API with include/exclude name patterns
- IUiDispatcher abstraction (and WpfUiDispatcher) to safely marshal updates to the UI thread
- Throttle helper utility to debounce PID/window reconciliation and avoid UI churn

### Changed
- PlayOnlineMonitorService now uses shared ProcessManagementService events for:
  - Character detection/removal
  - Real-time window title updates (with UpdateTitleIfChanged helper)
  - Centralized UI-thread dispatch when raising CharacterUpdated
- ExternalApplicationService aligned with unified process discovery:
  - Tracks PIDs on launch and untracks on termination
  - Maps processes back to application instances; emits ApplicationStatusChanged on transitions
- ViewModels simplified and made more robust:
  - PlayOnlineMonitorViewModel maintains an ObservableCollection and reacts to service events
  - ProfileManagementViewModel moves long-running operations off the UI thread, updating via dispatcher
- XAML UX improvements:
  - Compact CharacterMonitor window with status cues, quick switch button, and monitoring controls
  - ProfileActionsView shows richer status, tooltips, and action buttons with consistent styles
  - DisplayName binding ensures graceful fallback to WindowTitle when character metadata is missing

### Fixed
- Eliminated ad-hoc per-PID throttle dictionaries; replaced with a reusable Throttle utility
- Ensured PlayOnlineCharacter raises property change notifications for dependent fields (DisplayName, Status*)
- Reduced crashes from UI-thread violations by dispatching CharacterUpdated via IUiDispatcher
- Improved resilience around process/window enumeration with try/catch and logging

### Known issues / follow-ups
- ProcessManagementService still uses fire-and-forget Task.Run in several event handlers; replace with cancellable flows
- CancellationToken plumb-through for monitoring and enumeration is pending
- Logging categories and levels need standardization and minor refactor
- Discovery watch registration in ExternalApplicationService should be added/removed on Start/Stop
- Consider a WindowIdentity struct to stabilize window/handle association across re-parents

---

When merging, please:
- Update the Unreleased section with the final version and date
- Tag the release and attach binaries as applicable
- Ensure TODO.md items marked as [Done] reflect the code state after merge


# TODO: Code cleanup, simplification, and design improvements

Track progress here. Check items as we complete them, and add new items as needed.

## Core architecture and services
- [ ] Centralize throttling/debouncing
  - [ ] Introduce a small Throttle helper/service (e.g., `ThrottlePerKey(key, interval, action)`).
  - [ ] Replace ad-hoc per-PID throttle dictionaries in PlayOnlineMonitorService.
  - [ ] Use for any fallback window enumeration in ProcessManagementService.
- [ ] Event model and thread marshalling
  - [ ] Introduce an `IUiDispatcher` abstraction with `Invoke/BeginInvoke`.
  - [ ] Update services (ProcessManagementService, PlayOnlineMonitorService) to use it, avoiding direct `Application.Current` references.
- [ ] Avoid fire-and-forget `Task.Run`
  - [ ] Replace with explicit async handlers or background worker/queue; ensure try/catch with logging where fire-and-forget remains.
- [ ] Cancellation and disposal
  - [ ] Pass `CancellationToken` through global monitoring/polling.
  - [ ] Ensure WMI watchers and WinEvent hooks are disposed safely on Stop/Dispose.
  - [ ] Ensure discovery watches and per-PID tracking are unregistered on Stop/Dispose.
- [ ] Stronger process/window identity handling
  - [ ] Add a `WindowIdentity { int ProcessId; IntPtr Handle; }` with equality for consistent matching.
  - [ ] Utility to re-associate windows when handles change.
- [ ] Logging consistency
  - [ ] Wrap logging in a consistent interface + categories; ensure expected vs exceptional paths are logged at Debug/Info/Warning appropriately.

## PlayOnlineMonitorService
- [ ] Clarify update pathways
  - [ ] Document global vs per-PID update flows and ensure event handlers use per-PID path only.
- [ ] Consolidate title updates
  - [ ] Extract `UpdateTitleIfChanged` helper to update and raise `CharacterUpdated`.
  - [ ] Centralize UI-thread dispatch for all `CharacterUpdated` raises.
- [ ] Reduce redundant refresh
  - [ ] After per-PID updates, consider marking that PID as fresh to skip next global refresh (optional).

## ProcessManagementService
- [ ] Hooks lifecycle and reliability
  - [ ] Wrap WMI and WinEvent callbacks in try/catch and log.
  - [ ] Log init/teardown of hooks; optionally self-test and log status once on startup.
- [ ] Discovery API polish
  - [ ] DiscoveryFilter supports include/exclude patterns (simple wildcards or regex).
  - [ ] Ensure `TrackPid/UntrackPid` are idempotent and robust under churn.
- [ ] Window enumeration throttling
  - [ ] Throttle per-PID/window enumeration when title events are active.
  - [ ] Expose configurable RefreshInterval.

## ExternalApplicationService
- [ ] Align with unified discovery fully
  - [ ] Confirm discovery watch registration on Start and un-registration on Stop.
  - [ ] Use `TrackPid/UntrackPid` consistently; avoid redundant global re-enumeration.

## Models and ViewModels
- [ ] Observable updates
  - [ ] Ensure collections used by UI are ObservableCollection and modified on UI thread or with synchronization.
  - [ ] Verify `PlayOnlineCharacter` raises PropertyChanged for `WindowTitle` (and other bound properties).
- [ ] Immutable vs mutable model properties
  - [ ] Consider making identity properties immutable; encapsulate updates via methods to ensure notifications.

## XAML and UI
- [ ] Style/trigger cleanup and reuse
  - [ ] Continue replacing invalid triggers; extract common styles/resources.
  - [ ] Ensure the UI binds to `DisplayName` that falls back to `WindowTitle`.
- [ ] Diagnostics panel (optional)
  - [ ] Show hook status, last event time, tracked PID count when diagnostics enabled.

## Reliability and diagnostics
- [ ] Feature-flagged debug logging
  - [ ] Add a setting to enable verbose logging.
  - [ ] On verbose, log window title changes (PID/HWND, before/after) with throttling.
- [ ] Error handling policy
  - [ ] Standardize catch/log behavior and levels; add a helper/policy to keep consistent.

## Code quality tools
- [ ] Enable nullable reference types in csproj.
- [ ] Add .editorconfig and adopt `dotnet format`.
- [ ] Add Roslyn analyzers (Microsoft.CodeAnalysis.NetAnalyzers) and optionally StyleCop with a tuned ruleset.

## Testing
- [ ] Unit tests for per-PID update routine
  - [ ] Verify global refresh does not remove per-PID entries incorrectly; per-PID updates don’t prune other PIDs.
- [ ] Unit tests for title update flows
  - [ ] Ensure `CharacterUpdated` fires on title change and on periodic reconciliation.
- [ ] Abstract WMI/WinEvent behind interfaces for integration tests with fakes (where feasible).


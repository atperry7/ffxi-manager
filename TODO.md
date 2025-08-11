# TODO: Code cleanup, simplification, and design improvements

Track progress here. Check items as we complete them, and add new items as needed.

Prioritization legend:
- [Immediate] = ready for immediate action
- [Deferred] = lower-priority; schedule after immediate items
- Priority categories: [Critical Bug Fix], [UX Improvement], [Code Cleanup], [Future Enhancement]

## Core architecture and services
- [x] Centralize throttling/debouncing [Code Cleanup][Done]
  - [x] Introduce a small Throttle helper/service (e.g., `ThrottlePerKey(key, interval, action)`). [Code Cleanup][Done]
  - [x] Replace ad-hoc per-PID throttle dictionaries in PlayOnlineMonitorService. [Code Cleanup][Done]
  - [ ] Use for any fallback window enumeration in ProcessManagementService. [Code Cleanup][Immediate]
- [ ] Event model and thread marshalling [Critical Bug Fix][Immediate]
  - [x] Introduce an `IUiDispatcher` abstraction with `Invoke/BeginInvoke`. [Code Cleanup][Done]
  - [ ] Update services (ProcessManagementService, PlayOnlineMonitorService) to use it, avoiding direct `Application.Current` references. (PlayOnlineMonitorService done; ProcessManagementService pending) [Critical Bug Fix][Immediate]
- [ ] Avoid fire-and-forget `Task.Run` [Critical Bug Fix][Immediate]
  - [ ] Replace with explicit async handlers or background worker/queue; ensure try/catch with logging where fire-and-forget remains. [Critical Bug Fix][Immediate]
- [ ] Cancellation and disposal [Critical Bug Fix][Immediate]
  - [ ] Pass `CancellationToken` through global monitoring/polling. [Critical Bug Fix][Immediate]
  - [ ] Ensure WMI watchers and WinEvent hooks are disposed safely on Stop/Dispose. [Critical Bug Fix][Immediate]
  - [ ] Ensure discovery watches and per-PID tracking are unregistered on Stop/Dispose. [Critical Bug Fix][Immediate]
- [ ] Stronger process/window identity handling [Critical Bug Fix][Immediate]
  - [ ] Add a `WindowIdentity { int ProcessId; IntPtr Handle; }` with equality for consistent matching. [Critical Bug Fix][Immediate]
  - [ ] Utility to re-associate windows when handles change. [Critical Bug Fix][Deferred]
- [ ] Logging consistency [Code Cleanup][Immediate]
  - [ ] Wrap logging in a consistent interface + categories; ensure expected vs exceptional paths are logged at Debug/Info/Warning appropriately. [Code Cleanup][Immediate]

## PlayOnlineMonitorService
- [ ] Clarify update pathways [Code Cleanup][Deferred]
  - [ ] Document global vs per-PID update flows and ensure event handlers use per-PID path only. [Code Cleanup][Deferred]
- [x] Consolidate title updates [UX Improvement][Done]
  - [x] Extract `UpdateTitleIfChanged` helper to update and raise `CharacterUpdated`. [UX Improvement][Done]
  - [x] Centralize UI-thread dispatch for all `CharacterUpdated` raises. [Critical Bug Fix][Done]
- [ ] Reduce redundant refresh [UX Improvement][Deferred]
  - [ ] After per-PID updates, consider marking that PID as fresh to skip next global refresh (optional). [UX Improvement][Deferred]

## ProcessManagementService
- [ ] Hooks lifecycle and reliability [Critical Bug Fix][Immediate]
  - [ ] Wrap WMI and WinEvent callbacks in try/catch and log. [Critical Bug Fix][Immediate]
  - [ ] Log init/teardown of hooks; optionally self-test and log status once on startup. [Critical Bug Fix][Immediate]
- [ ] Discovery API polish [Code Cleanup][Deferred]
  - [ ] DiscoveryFilter supports include/exclude patterns (simple wildcards or regex). [Future Enhancement][Deferred]
  - [ ] Ensure `TrackPid/UntrackPid` are idempotent and robust under churn. [Critical Bug Fix][Immediate]
- [ ] Window enumeration throttling [UX Improvement][Immediate]
  - [ ] Throttle per-PID/window enumeration when title events are active. [UX Improvement][Immediate]
  - [ ] Expose configurable RefreshInterval. [UX Improvement][Immediate]

## ExternalApplicationService
- [ ] Align with unified discovery fully [Code Cleanup][Immediate]
  - [ ] Confirm discovery watch registration on Start and un-registration on Stop. [Critical Bug Fix][Immediate]
  - [x] Use `TrackPid/UntrackPid` consistently; avoid redundant global re-enumeration. [Code Cleanup][Done]

## Models and ViewModels
- [x] Observable updates [UX Improvement][Immediate]
  - [x] Ensure collections used by UI are ObservableCollection and modified on UI thread or with synchronization. [UX Improvement][Done]
  - [x] Verify `PlayOnlineCharacter` raises PropertyChanged for `WindowTitle` (and other bound properties). [UX Improvement][Done]
- [ ] Immutable vs mutable model properties [Code Cleanup][Deferred]
  - [ ] Consider making identity properties immutable; encapsulate updates via methods to ensure notifications. [Code Cleanup][Deferred]

## XAML and UI
- [ ] Style/trigger cleanup and reuse [UX Improvement][Immediate]
  - [ ] Continue replacing invalid triggers; extract common styles/resources. [UX Improvement][Immediate]
  - [x] Ensure the UI binds to `DisplayName` that falls back to `WindowTitle`. [UX Improvement][Done]
- [ ] Diagnostics panel (optional) [Future Enhancement][Deferred]
  - [ ] Show hook status, last event time, tracked PID count when diagnostics enabled. [Future Enhancement][Deferred]

## Reliability and diagnostics
- [ ] Feature-flagged debug logging [Future Enhancement][Deferred]
  - [ ] Add a setting to enable verbose logging. [Future Enhancement][Deferred]
  - [ ] On verbose, log window title changes (PID/HWND, before/after) with throttling. [Future Enhancement][Deferred]
- [ ] Error handling policy [Critical Bug Fix][Immediate]
  - [ ] Standardize catch/log behavior and levels; add a helper/policy to keep consistent. [Critical Bug Fix][Immediate]

## Code quality tools
- [ ] Enable nullable reference types in csproj. [Code Cleanup][Deferred]
- [ ] Add .editorconfig and adopt `dotnet format`. [Code Cleanup][Deferred]
- [ ] Add Roslyn analyzers (Microsoft.CodeAnalysis.NetAnalyzers) and optionally StyleCop with a tuned ruleset. [Code Cleanup][Deferred]

## Testing
- [ ] Unit tests for per-PID update routine [Code Cleanup][Deferred]
  - [ ] Verify global refresh does not remove per-PID entries incorrectly; per-PID updates don’t prune other PIDs. [Code Cleanup][Deferred]
- [ ] Unit tests for title update flows [Code Cleanup][Deferred]
  - [ ] Ensure `CharacterUpdated` fires on title change and on periodic reconciliation. [Code Cleanup][Deferred]
- [ ] Abstract WMI/WinEvent behind interfaces for integration tests with fakes (where feasible). [Code Cleanup][Deferred]

---

## New tasks discovered during cleanup
- [Immediate][UX/Arch] Replace remaining direct uses of `Application.Current.Dispatcher` in ViewModels with `IUiDispatcher` to centralize UI thread marshalling.
- [Immediate][Reliability] Introduce `CancellationToken` flow for monitoring tasks; avoid fire-and-forget `Task.Run` in services.
- [Immediate][UX] Add a user-configurable setting for global process monitor interval; surface in Settings UI and wire to `StartGlobalMonitoring`.
- [Immediate][Infra] Register a discovery watch in ExternalApplicationService.StartMonitoring and unregister in StopMonitoring for tighter scope.
- [Deferred][Infra] Implement `WindowIdentity` struct and helpers to re-associate windows when handles change.
- [Deferred][Docs] Add CONTRIBUTING.md and document release process (tagging, packaging, and CHANGELOG update workflow).

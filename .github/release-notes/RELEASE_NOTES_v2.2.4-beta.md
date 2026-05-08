# FFXI Manager v2.2.4-beta

## Overview
This beta release fixes a major regression in hotkey-based character switching that surfaced after the April 2026 Windows 11 security updates (KB5083769 / KB5082417), and a long-standing UI annoyance where the External Applications panel showed a scrollbar with only four apps configured.

## What's Fixed

### Critical Bug Fixes
- **Fixed hotkey input leaking to previously-activated character windows ([#12](https://github.com/aeperry/FFXIManager/issues/12))**: After hotkey-switching from one character to another, keystrokes meant for the new character were also being sent to all previously-activated character windows. The issue surfaced after the Win11 KB5083769 / KB5082417 updates tightened foreground-window behavior, causing thread input attachments to release more slowly than the activation path assumed. Alt-Tabbing or mouse-clicking a window cleared it temporarily — now the activation path itself prevents the leak.
- **Fixed Apps panel scrollbar appearing prematurely with 4 entries ([#11](https://github.com/aeperry/FFXIManager/issues/11))**: The External Applications panel was hard-capped to a 300px-tall scroll area, which combined with the larger circular button styles introduced in v2.2.x meant a vertical scrollbar appeared as soon as a 4th app was added. The list now grows naturally inside the available pane, matching the layout of the Monitor and Queue tabs, and only scrolls on genuine overflow.

## Technical Details

### Hotkey activation rework (`Infrastructure/ProcessUtilityService.cs`)
- **Reordered activation strategies** to defer the `AttachThreadInput`-based path. Activations now attempt `SimpleActivation` → `SwitchActivation` → `ThreadAttachmentActivation`, so the bug-prone path is the last resort instead of the second.
- **Replaced `AggressiveActivation` with `SwitchActivation`**, swapping the global `SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT)` mutation for the per-call `LockSetForegroundWindow(LSFW_UNLOCK)` bypass. This eliminates a crash-unsafe restore-in-finally pattern that could leave the system-wide foreground-lock timeout at zero if the activation was interrupted.
- **Added defensive thread-input detachment** at the entry of every activation. The most recently `AttachThreadInput`-attached target thread is tracked and force-detached before the next activation, breaking attachment chains that the kernel hasn't fully released yet.
- **Added a 15ms in-lock settle delay** after a successful activation so the OS input router can fully retire the prior foreground assignment before the next hotkey press begins.
- **Upgraded the per-attempt success log from Debug to Info** — beta testers can now confirm activations are succeeding on the lighter attempts (`attempt 1/3` or `2/3`) without enabling debug logging. Frequent `attempt 3/3` would indicate the heavier fallback is still being exercised.

### Apps panel layout fix (`Views/ProfileActionsView.xaml`)
- **Replaced the `StackPanel`-with-nested-`ScrollViewer` layout with a `DockPanel`**: Add/Refresh buttons dock at the bottom; the apps list `ScrollViewer` fills the remaining space. The hardcoded `MaxHeight=300` is gone, so the scrollbar appears only when the list actually outgrows the pane.

## Known Issues
None specific to this release.

## Installation

Download `FFXIManager-v2.2.4-beta-release-package.zip` from the assets below.

1. Extract the outer zip
2. Verify SHA256 checksum (optional but recommended)
3. Extract the inner `FFXIManager-v2.2.4-beta-windows.zip`
4. Run `FFXIManager.exe`

## Beta Testing Notes
Please test the following scenarios and report any issues:

### Hotkey switching ([#12](https://github.com/aeperry/FFXIManager/issues/12))
1. Launch FFXI Manager, start Windower and POL Proxy.
2. Log in 4–6 characters.
3. Bind hotkeys to each character and switch between them in rapid succession (e.g., Win+F1 → Win+F2 → Win+F3 → …).
4. Type a unique short string in each character's chat (e.g., `/echo char1`, `/echo char2`) and confirm the message lands only on the intended character — not on prior characters.
5. After testing, attach a recent log file from `%APPDATA%/FFXIManager/logs/log-YYYYMMDD.json`. Grep for `Window activation succeeded on attempt` to see which path is winning. Most should be `attempt 1/3` or `2/3`.

### Apps panel scrollbar ([#11](https://github.com/aeperry/FFXIManager/issues/11))
1. Open the 🚀 Apps tab.
2. Add a 4th application — confirm there is **no scrollbar**.
3. Continue adding apps until the list genuinely overflows the pane — confirm the scrollbar appears only then, and that the Add (➕) and Refresh (🔄) buttons stay pinned at the bottom and remain visible.
4. Resize the main window vertically and confirm the apps list responds appropriately.

---

**Full Changelog**: https://github.com/aeperry/FFXIManager/compare/v2.2.3-beta...v2.2.4-beta

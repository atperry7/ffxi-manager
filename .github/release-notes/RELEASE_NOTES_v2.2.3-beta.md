# FFXI Manager v2.2.3-beta

## Overview
This beta release adds keyboard-based character slot navigation as an alternative to click-based selection, providing a more reliable method for selecting FFXI character slots during auto-login.

## What's New

### Features
- **Keyboard-based character slot navigation**: New `CharacterSlotKeyboard` action type that uses arrow keys instead of mouse clicks to navigate character selection
  - Optimal pathing: Uses Down arrow for slots 1-8, Up arrow for slots 10-16 (shortest path)
  - No template matching required - simpler and more reliable than click-based approach
  - Available in the Workflow Editor alongside existing `CharacterSlot` (click-based) action

### Technical Details
- Added `CharacterSlotKeyboardActionExecutor` with circular navigation logic (16-slot wraparound)
- Integrated with existing workflow action factory - auto-discovered via DI
- Uses existing DirectX-compatible keyboard timing (100ms arrow key hold)

## Installation

Download `FFXIManager-v2.2.3-beta-release-package.zip` from the assets below.

1. Extract the outer zip
2. Verify SHA256 checksum (optional but recommended)
3. Extract the inner `FFXIManager-v2.2.3-beta-windows.zip`
4. Run `FFXIManager.exe`

## Beta Testing Notes
Please test the following scenarios and report any issues:
1. Using `CharacterSlotKeyboard` action in workflows for different slot numbers (1-16)
2. Comparing reliability vs click-based `CharacterSlot` action
3. Auto-login with accounts using various character slots

---

**Full Changelog**: https://github.com/aeperry/FFXIManager/compare/v2.2.2-beta...v2.2.3-beta

# FFXI Manager v2.2.0-beta

## Overview
This pre-release focuses on workflow management enhancements, performance analytics, and architectural improvements to the auto-login system. Major additions include workflow import/export capabilities and granular performance tracking for workflow optimization.

## What's New

### Features
- **Workflow Import/Export** - Share and import workflow configurations across installations, enabling easier community collaboration and backup strategies
- **Granular Performance Analytics** - Detailed workflow execution statistics and timing data to help optimize login sequences
- **Enhanced Statistics Dashboard** - Refactored statistics UI with improved click point visualization and action sequence timing insights

### Improvements
- **Unified Retry/Repeat/Delay Architecture** - All action executors now use consistent retry, repeat, and delay logic for improved reliability
- **Action Timing in Retry Budget** - Action sequence execution times are now properly included in retry timeout calculations for more accurate timeouts
- **Streamlined Hotkey System** - Removed legacy flood and emergency protection code for cleaner, more maintainable hotkey handling
- **Workflow Validation** - Prevents multiple workflows from being marked as default (IsDefault=true), avoiding configuration conflicts
- **UI Polish** - Status messages now appear at the top of the popup window (newest first) for better visibility

### Configuration Updates
- Adjusted `PostNavigationDelayMs` timing in workflow steps for optimal navigation reliability
- Updated `CharacterSlot` count to 1 in default workflow configuration
- Refined `ItemsPanel` trigger threshold in CharacterMonitorWindowV2

### Internal Improvements
- ActionExecutor architecture refactoring following SOLID principles
- Improved workflow step timing and execution consistency
- Better separation of concerns in action execution pipeline

## Installation

Download `FFXIManager-v2.2.0-beta-release-package.zip` from the assets below.

1. Extract the outer zip
2. Verify SHA256 checksum (optional but recommended)
3. Extract the inner `FFXIManager-v2.2.0-beta-windows.zip`
4. Run `FFXIManager.exe`

## Upgrade Notes

This is a pre-release for testing. If you encounter issues:
- Check logs in `%APPDATA%\FFXIManager\logs\`
- Existing workflow configurations should work without modification
- Workflow import/export uses JSON format for easy sharing

## Testing Focus

As a beta release, please test:
- Workflow import/export functionality
- Performance analytics accuracy
- Retry/timeout behavior with the new unified architecture
- Any workflow configurations you use regularly

---

**Full Changelog**: https://github.com/YourUsername/FFXIManager/compare/v2.1.0-beta...v2.2.0-beta

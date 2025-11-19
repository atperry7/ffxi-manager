# FFXI Manager v2.2.2-beta

## Overview
This beta release fixes critical OTP (One-Time Password) functionality issues that prevented proper display and auto-login integration for accounts with two-factor authentication.

## What's Fixed

### Critical Bug Fixes
- **Fixed OTP "No Key" display issue**: OTP codes now display correctly after adding authentication keys to accounts. Previously, the UI would show "No Key" even after successfully storing the OTP secret in Windows Credential Manager.
- **Fixed OTP auto-login integration**: Auto-login now generates fresh TOTP codes on-demand during execution, eliminating dependency on pre-populated UI state. Accounts with OTP enabled will now successfully authenticate during automated login sequences.
- **Fixed OTP state synchronization**: Account loading now properly syncs `HasStoredSecret` status with Windows Credential Manager, ensuring UI accurately reflects whether OTP credentials are stored.

### Technical Improvements
- Added on-demand OTP code generation in `InputOTPActionExecutor` for reliable auto-login execution
- Implemented property change notification chain for nested `OTPConfiguration` updates in account models
- Added Windows Credential Manager verification during account loading to maintain state consistency
- Improved boolean logic clarity in `OTPCodeDisplay` property evaluation

## Architecture Changes
This release includes significant improvements to OTP data flow:
- **Auto-login path**: Generates TOTP codes fresh at execution time from Windows Credential Manager
- **UI display path**: Independent timer-based refresh for manual viewing, with proper property change notifications
- **Account loading**: Validates OTP credential existence against Windows Credential Manager on every load

## Known Issues
None specific to this release.

## Installation

Download `FFXIManager-v2.2.2-beta-release-package.zip` from the assets below.

1. Extract the outer zip
2. Verify SHA256 checksum (optional but recommended)
3. Extract the inner `FFXIManager-v2.2.2-beta-windows.zip`
4. Run `FFXIManager.exe`

## Beta Testing Notes
Please test the following scenarios and report any issues:
1. Adding OTP authentication keys to new and existing accounts
2. Verifying OTP code display in the UI (both masked and visible states)
3. Auto-login functionality with OTP-enabled accounts
4. OTP state persistence across application restarts

---

**Full Changelog**: https://github.com/aeperry/FFXIManager/compare/v2.2.1-beta...v2.2.2-beta

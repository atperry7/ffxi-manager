# Resolution/DPI Independent Auto-Login Implementation Guide

## Status: Phase 1 Complete ✅

This document tracks the implementation of resolution and DPI independent auto-login functionality.

---

## Phase 1: Foundation Architecture (COMPLETED ✅)

### What Was Implemented

#### 1. Navigation Strategy Pattern
Created a flexible strategy pattern for UI navigation:

- **`INavigationStrategy`** - Interface for navigation strategies
- **`KeyboardNavigationStrategy`** - Resolution-independent keyboard navigation
- **`RelativeClickNavigationStrategy`** - DPI-aware clicking with relative positioning
- **`HybridNavigationStrategy`** - Keyboard first, click fallback

**Location:** `Services/AutoLogin/Navigation/`

#### 2. Navigation Models
Created data models for navigation configuration:

- **`NavigationAction`** - Main navigation configuration
- **`KeyboardAction`** - Individual keyboard step definition
- **`RelativeClickOffset`** - Relative click coordinates (0.0 to 1.0 scale)
- **`NavigationType`** - Enum for strategy types (Keyboard, RelativeClick, Hybrid)

**Location:** `Models/NavigationAction.cs`

#### 3. BaseLoginTaskHandler Enhancements
Added new methods to support navigation strategies:

- **`ExecuteNavigationFromTemplateAsync()`** - Reads navigation from template metadata
- **`ExecuteNavigationActionAsync()`** - Executes navigation using appropriate strategy
- **`CalculateRelativeClickPoint()`** - Calculates scaled coordinates

**Location:** `Services/AutoLogin/BaseLoginTaskHandler.cs`

#### 4. TemplateMetadata Extension
Extended template metadata to include navigation configuration:

- **`Navigation`** property added to `TemplateMetadata` class
- Supports new navigation models
- Backward compatible with existing `Action` configuration

**Location:** `Models/ScreenDetection/TemplateMetadata.cs`

---

## Phase 2: Manual Discovery & Configuration (NEXT STEP 📝)

### Objective
Document PlayOnline keyboard navigation sequences through manual testing.

### Testing Procedure

#### Setup
1. Launch PlayOnline manually
2. Have a notepad ready to record key sequences
3. Test at your current resolution (3840x2160 @ 1.5 DPI)

#### Screens to Document

##### 1. Member Selection Screen
- **Test:** How many Tab presses to reach member slot 1?
- **Test:** Does Down Arrow navigate between slots?
- **Test:** Does Enter select the highlighted slot?
- **Record:**
  ```
  Member Slot 1: [Your findings]
  Member Slot 2: [Your findings]
  Member Slot 3: [Your findings]
  Member Slot 4: [Your findings]
  ```

##### 2. Login Information Screen
- **Test:** Tab to "Log In" button
- **Test:** Can Enter activate it?
- **Record:**
  ```
  Navigation to Login: [Your findings]
  Activation method: [Your findings]
  ```

##### 3. Connect to PlayOnline Screen
- **Test:** Tab to password field
- **Test:** Does virtual keyboard appear on click only?
- **Test:** Can Tab navigate away from password field?
- **Record:**
  ```
  Navigation to password field: [Your findings]
  Virtual keyboard behavior: [Your findings]
  Navigation after password entry: [Your findings]
  ```

##### 4. OTP Entry Screen (if applicable)
- **Test:** Tab to OTP field
- **Test:** Can you navigate between fields?
- **Record:**
  ```
  Navigation to OTP field: [Your findings]
  Tab order: [Your findings]
  ```

##### 5. Main Menu / Game Selection
- **Test:** Arrow keys to navigate menu
- **Test:** Tab vs Arrow navigation
- **Test:** Enter to select
- **Record:**
  ```
  Navigation method: [Your findings]
  Selection method: [Your findings]
  ```

##### 6. Play Screen
- **Test:** Tab to "Play" button
- **Test:** Enter to activate
- **Record:**
  ```
  Navigation: [Your findings]
  ```

---

## Phase 2: Template JSON Examples

### Example: Member Selection with Keyboard Navigation

**File:** `Templates/PlayOnline/member_selection_screen.json`

```json
{
  "name": "member_selection_screen",
  "templatePath": "PlayOnline/member_selection_screen.png",
  "elementType": "screen",
  "associatedStep": "MemberSelection",
  "confidenceThreshold": 0.85,
  "version": "2.0.0",
  "navigation": {
    "type": "Hybrid",
    "description": "Navigate to and select member slot",
    "sequence": [
      {
        "action": "Tab",
        "count": 2,
        "delayMs": 150,
        "description": "Navigate to member selection area"
      },
      {
        "action": "Enter",
        "count": 1,
        "delayMs": 500,
        "description": "Select member slot 1 (default)"
      }
    ],
    "postNavigationDelayMs": 1000,
    "fallback": {
      "type": "RelativeClick",
      "clickOffset": {
        "x": 0.5,
        "y": 0.3,
        "description": "Click center-top of member selection area"
      }
    }
  },
  "action": {
    "type": "click",
    "clickOffset": {
      "x": 960,
      "y": 540
    }
  }
}
```

### Example: Login Button with Keyboard-Only Navigation

```json
{
  "name": "login_button",
  "templatePath": "PlayOnline/login_information_screen.png",
  "elementType": "button",
  "associatedStep": "PasswordEntry",
  "confidenceThreshold": 0.80,
  "version": "2.0.0",
  "navigation": {
    "type": "Keyboard",
    "description": "Navigate to and click Login button",
    "sequence": [
      {
        "action": "Tab",
        "count": 3,
        "delayMs": 100,
        "description": "Tab to Login button"
      },
      {
        "action": "Enter",
        "count": 1,
        "delayMs": 500,
        "description": "Activate Login button"
      }
    ],
    "postNavigationDelayMs": 1000
  }
}
```

### Example: Relative Click with Template Anchoring

```json
{
  "name": "windower_launch_arrow",
  "templatePath": "Windower/launch_arrow.png",
  "elementType": "button",
  "associatedStep": "ClickLaunchButton",
  "confidenceThreshold": 0.85,
  "version": "2.0.0",
  "navigation": {
    "type": "RelativeClick",
    "description": "Click launch arrow (relative to detected template)",
    "clickOffset": {
      "x": 0.5,
      "y": 0.5,
      "description": "Click center of detected launch arrow"
    },
    "postNavigationDelayMs": 2000
  }
}
```

---

## Phase 3: Integration Changes (COMPLETED ✅)

### BaseLoginTaskHandler Methods

#### ExecuteNavigationFromTemplateAsync
```csharp
// NEW PREFERRED METHOD - Replaces direct clicking
protected async Task<bool> ExecuteNavigationFromTemplateAsync(
    AutoLoginSubtask subtask,
    string templatePath,
    IntPtr windowHandle,
    TemplateMatchResult templateMatch,
    IUIAutomationService automationService,
    CancellationToken cancellationToken)
```

**Usage Example:**
```csharp
// Old way (hard-coded coordinates):
await ClickAtCoordinatesAsync(subtask, new Point(960, 540), windowHandle, "member slot", ct, _automationService);

// New way (resolution-independent):
var memberScreenMatch = await WaitForScreenDetectionAsync(...);
bool success = await ExecuteNavigationFromTemplateAsync(
    subtask,
    "PlayOnline/member_selection_screen",
    windowHandle,
    memberScreenMatch,
    _automationService,
    cancellationToken);
```

---

## Phase 4: Handler Updates (TODO)

### PlayOnlineAuthHandler Changes Needed

#### Current Click-Based Locations
1. **Member slot selection** (Line 298-306)
   - Replace with keyboard navigation
   - Fallback to relative click

2. **"Log In" button** (Lines 407-413)
   - Replace with keyboard navigation

3. **Password field** (Lines 468-474)
   - Replace with keyboard navigation (if possible)
   - May need to keep click for virtual keyboard trigger

4. **Virtual keyboard password field** (Lines 501-507)
   - Evaluate if keyboard can activate
   - Keep click as fallback

5. **Connect button** (Lines 585-607)
   - Already uses ClickAtTemplateCoordinatesAsync
   - Update to use ExecuteNavigationFromTemplateAsync

6. **FFXI game selection** (Lines 1016-1022)
   - Replace with arrow key navigation

#### Migration Pattern
```csharp
// BEFORE:
await ClickAtCoordinatesAsync(
    subtask,
    PlayOnlineAuthConfiguration.Coordinates.MemberSlots.GetSlotCoordinate(memberSlot),
    windowHandle,
    $"member slot {memberSlot}",
    cancellationToken,
    _automationService);

// AFTER:
var memberScreenMatch = await WaitForScreenDetectionAsync(...);
bool navigationSuccess = await ExecuteNavigationFromTemplateAsync(
    subtask,
    "PlayOnline/member_selection_screen",
    windowHandle,
    memberScreenMatch,
    _automationService,
    cancellationToken);

if (!navigationSuccess)
{
    await _loggingService.LogWarningAsync("Navigation failed, operation may not complete");
    // Handler-specific error recovery
}
```

---

## Testing Matrix (Phase 6)

### Resolutions to Test
- [  ] 3840x2160 (4K) - Currently working
- [  ] 2560x1440 (QHD)
- [  ] 1920x1080 (Full HD)
- [  ] 1366x768 (HD)

### DPI Settings to Test (per resolution)
- [  ] 100% (No scaling)
- [  ] 125% (Recommended for many monitors)
- [  ] 150% (Current working setting)
- [  ] 175% (High DPI)

### Test Scenarios
For each resolution/DPI combination:

1. **Full Login Flow**
   - [  ] Windower launch
   - [  ] PlayOnline authentication
   - [  ] Member selection (all 4 slots)
   - [  ] Password entry
   - [  ] OTP entry (if enabled)
   - [  ] Game selection and launch
   - [  ] FFXI character selection
   - [  ] Complete login

2. **Navigation Strategy Validation**
   - [  ] Keyboard navigation succeeds
   - [  ] Fallback to clicking (if keyboard fails)
   - [  ] Relative click coordinates are accurate
   - [  ] No off-screen clicks

3. **Edge Cases**
   - [  ] Window repositioning mid-login
   - [  ] Multiple monitors
   - [  ] Different aspect ratios

---

## Success Criteria

### Phase 1 ✅
- [x] Navigation strategy architecture implemented
- [x] Models and interfaces defined
- [x] BaseLoginTaskHandler enhanced
- [x] TemplateMetadata extended

### Phase 2 (Manual Discovery) 📝
- [ ] Keyboard navigation sequences documented for all PlayOnline screens
- [ ] Tab order confirmed for each screen
- [ ] Arrow key behavior documented
- [ ] Enter key functionality validated
- [ ] Findings recorded in testing notes

### Phase 3 (Templates) 📝
- [ ] All template JSON files updated with navigation metadata
- [ ] Keyboard sequences configured
- [ ] Relative click offsets defined
- [ ] Hybrid strategies configured where appropriate

### Phase 4 (Handlers) 📝
- [ ] PlayOnlineAuthHandler migrated
- [ ] WindowerLaunchHandler migrated
- [ ] FFXIGameHandler formalized
- [ ] All hard-coded coordinates removed

### Phase 5 (Configuration) 📝
- [ ] Coordinates removed from PlayOnlineAuthConfiguration
- [ ] Coordinates removed from WindowerLaunchConfiguration
- [ ] Configuration classes cleaned up

### Phase 6 (Testing) 📝
- [ ] All resolution/DPI combinations tested
- [ ] Success rate: 95%+ across all configurations
- [ ] Fallback mechanisms validated
- [ ] Integration tests updated

### Phase 7 (Production) 📝
- [ ] Feature flags implemented
- [ ] Enhanced logging in place
- [ ] Documentation complete
- [ ] Ready for release

---

## Next Steps

1. **You must manually test PlayOnline keyboard navigation** (Phase 2)
   - Follow the testing procedure above
   - Record your findings in this document
   - This will inform the template JSON updates

2. **Update template JSON files** (Phase 2)
   - Use the examples above as templates
   - Add navigation metadata to all templates
   - Test JSON validity

3. **Review and approve handler migration plan** (Phase 4)
   - We'll update handlers to use new navigation methods
   - Preserve existing functionality as fallback
   - Test incrementally

---

## Questions? Issues?

### Common Issues

**Q: Keyboard navigation doesn't work for a specific screen**
- **A:** Configure that template to use `RelativeClick` type instead of `Keyboard`
- **A:** Or use `Hybrid` to try keyboard first, then fallback to clicking

**Q: Click coordinates are still off**
- **A:** Ensure `clickOffset` uses relative values (0.0 to 1.0)
- **A:** Verify the template is being detected correctly
- **A:** Check that template match region matches the clickable element

**Q: How do I test a specific navigation strategy?**
- **A:** Update the template JSON `navigation.type` field:
  - `"Keyboard"` - Keyboard only
  - `"RelativeClick"` - Click only
  - `"Hybrid"` - Try keyboard, fallback to click

---

## File Structure Summary

```
FFXIManager/
├── Models/
│   ├── NavigationAction.cs                    ✅ NEW
│   └── ScreenDetection/
│       └── TemplateMetadata.cs                 ✅ UPDATED
├── Services/
│   └── AutoLogin/
│       ├── BaseLoginTaskHandler.cs             ✅ UPDATED
│       ├── Navigation/                         ✅ NEW
│       │   ├── INavigationStrategy.cs
│       │   ├── KeyboardNavigationStrategy.cs
│       │   ├── RelativeClickNavigationStrategy.cs
│       │   └── HybridNavigationStrategy.cs
│       ├── PlayOnlineAuthHandler.cs            📝 TODO
│       ├── WindowerLaunchHandler.cs            📝 TODO
│       └── FFXIGameHandler.cs                  📝 TODO
└── Templates/                                   📝 TODO
    ├── PlayOnline/*.json
    ├── Windower/*.json
    └── FFXI/*.json
```

**Legend:**
- ✅ Completed
- 📝 Pending / In Progress
- ⚠️ Blocked / Issues

---

## Timeline Estimate

- **Phase 1:** ✅ 2 days (COMPLETED)
- **Phase 2:** 📝 2-3 days (Manual testing + JSON updates)
- **Phase 3:** ✅ 2-3 days (Infrastructure - COMPLETED)
- **Phase 4:** 📝 2-3 days (Handler migration)
- **Phase 5:** 📝 1 day (Configuration cleanup)
- **Phase 6:** 📝 3-4 days (Multi-resolution testing)
- **Phase 7:** 📝 1-2 days (Production polish)

**Total:** 12-18 days from start
**Remaining:** ~10-16 days

---

_Last Updated: [Automated by implementation]_
_Current Phase: Phase 1 Complete, Phase 2 Ready to Begin_

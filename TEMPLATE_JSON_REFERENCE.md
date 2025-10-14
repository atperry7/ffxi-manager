# Template JSON Navigation Reference Guide

## Quick Reference for Template Metadata Configuration

This guide provides examples and reference for configuring navigation in template JSON files.

---

## Navigation Types

### 1. Keyboard Navigation (Preferred)
**Resolution/DPI independent** - Works on any resolution

```json
{
  "navigation": {
    "type": "Keyboard",
    "description": "Human-readable description",
    "sequence": [
      {
        "action": "Tab",
        "count": 2,
        "delayMs": 100,
        "description": "Tab twice to reach button"
      },
      {
        "action": "Enter",
        "count": 1,
        "delayMs": 500,
        "description": "Activate button"
      }
    ],
    "postNavigationDelayMs": 1000
  }
}
```

### 2. Relative Click Navigation
**DPI-aware clicking** - Scales with resolution

```json
{
  "navigation": {
    "type": "RelativeClick",
    "description": "Click relative to detected template",
    "clickOffset": {
      "x": 0.5,
      "y": 0.5,
      "description": "Click center of template"
    },
    "postNavigationDelayMs": 500
  }
}
```

### 3. Hybrid Navigation (Recommended)
**Best of both** - Try keyboard, fallback to click

```json
{
  "navigation": {
    "type": "Hybrid",
    "description": "Keyboard first, click fallback",
    "sequence": [
      {
        "action": "Tab",
        "count": 3,
        "delayMs": 100
      },
      {
        "action": "Enter",
        "count": 1,
        "delayMs": 500
      }
    ],
    "fallback": {
      "type": "RelativeClick",
      "clickOffset": {
        "x": 0.5,
        "y": 0.3,
        "description": "Click upper-center of element"
      }
    },
    "postNavigationDelayMs": 1000
  }
}
```

---

## Supported Keyboard Actions

### Action Names (case-insensitive)
- `"Enter"` or `"Return"` - Enter/Return key
- `"Tab"` - Tab key
- `"DownArrow"` or `"Down"` - Down arrow
- `"UpArrow"` or `"Up"` - Up arrow
- `"LeftArrow"` or `"Left"` - Left arrow
- `"RightArrow"` or `"Right"` - Right arrow
- `"Escape"` or `"Esc"` - Escape key
- `"Spacebar"` or `"Space"` - Spacebar
- `"Home"` - Home key
- `"End"` - End key
- `"PageUp"` - Page Up key
- `"PageDown"` - Page Down key

### Keyboard Action Properties
```json
{
  "action": "Tab",           // Required: Action name
  "count": 2,                // Required: Repeat count (default: 1)
  "delayMs": 100,            // Required: Delay after action (default: 100)
  "description": "Optional"  // Optional: What this does
}
```

---

## Relative Click Offset Guide

### Coordinate System
- **X axis:** 0.0 = left edge, 0.5 = center, 1.0 = right edge
- **Y axis:** 0.0 = top edge, 0.5 = center, 1.0 = bottom edge

### Common Click Positions
```json
// Top-left corner
{ "x": 0.0, "y": 0.0 }

// Top-center
{ "x": 0.5, "y": 0.0 }

// Top-right corner
{ "x": 1.0, "y": 0.0 }

// Center-left
{ "x": 0.0, "y": 0.5 }

// Dead center (most common)
{ "x": 0.5, "y": 0.5 }

// Center-right
{ "x": 1.0, "y": 0.5 }

// Bottom-left
{ "x": 0.0, "y": 1.0 }

// Bottom-center
{ "x": 0.5, "y": 1.0 }

// Bottom-right
{ "x": 1.0, "y": 1.0 }

// Slightly off-center (example: upper-left of center)
{ "x": 0.4, "y": 0.4 }
```

---

## Complete Template Examples

### Example 1: Member Selection (Slot 1)
```json
{
  "name": "member_slot_1",
  "templatePath": "PlayOnline/member_selection_screen.png",
  "elementType": "button",
  "associatedStep": "MemberSelection",
  "confidenceThreshold": 0.85,
  "tolerance": 5,
  "version": "2.0.0",
  "navigation": {
    "type": "Hybrid",
    "description": "Select member slot 1 using Tab+Enter, fallback to click",
    "sequence": [
      {
        "action": "Tab",
        "count": 1,
        "delayMs": 150,
        "description": "Tab to member slot area"
      },
      {
        "action": "Enter",
        "count": 1,
        "delayMs": 500,
        "description": "Select default slot (slot 1)"
      }
    ],
    "fallback": {
      "type": "RelativeClick",
      "clickOffset": {
        "x": 0.5,
        "y": 0.2,
        "description": "Click upper-center of first slot"
      }
    },
    "postNavigationDelayMs": 1000
  },
  "action": {
    "type": "click",
    "clickOffset": { "x": 960, "y": 300 },
    "parameters": {}
  }
}
```

### Example 2: Password Field Activation
```json
{
  "name": "password_field",
  "templatePath": "PlayOnline/connect_screen.png",
  "elementType": "input",
  "associatedStep": "PasswordEntry",
  "confidenceThreshold": 0.80,
  "version": "2.0.0",
  "navigation": {
    "type": "Keyboard",
    "description": "Tab to password field",
    "sequence": [
      {
        "action": "Tab",
        "count": 2,
        "delayMs": 100,
        "description": "Navigate to password field"
      }
    ],
    "postNavigationDelayMs": 500
  }
}
```

### Example 3: FFXI Game Selection
```json
{
  "name": "ffxi_game_menu",
  "templatePath": "PlayOnline/main_screen.png",
  "elementType": "menu",
  "associatedStep": "PasswordEntry",
  "confidenceThreshold": 0.80,
  "version": "2.0.0",
  "navigation": {
    "type": "Keyboard",
    "description": "Navigate to FINAL FANTASY XI and select",
    "sequence": [
      {
        "action": "DownArrow",
        "count": 1,
        "delayMs": 150,
        "description": "Navigate to FFXI in menu"
      },
      {
        "action": "Enter",
        "count": 1,
        "delayMs": 500,
        "description": "Select FFXI"
      }
    ],
    "postNavigationDelayMs": 1000
  }
}
```

### Example 4: Windower Launch Arrow (Click-Only)
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
    "description": "Click Windower launch arrow",
    "clickOffset": {
      "x": 0.5,
      "y": 0.5,
      "description": "Click center of launch arrow"
    },
    "postNavigationDelayMs": 2000
  }
}
```

### Example 5: OTP Entry Field
```json
{
  "name": "otp_field",
  "templatePath": "PlayOnline/otp_screen.png",
  "elementType": "input",
  "associatedStep": "OTPEntry",
  "confidenceThreshold": 0.80,
  "version": "2.0.0",
  "navigation": {
    "type": "Hybrid",
    "description": "Navigate to OTP field",
    "sequence": [
      {
        "action": "Tab",
        "count": 1,
        "delayMs": 100,
        "description": "Tab to OTP field"
      }
    ],
    "fallback": {
      "type": "RelativeClick",
      "clickOffset": {
        "x": 0.5,
        "y": 0.5,
        "description": "Click center of OTP field"
      }
    },
    "postNavigationDelayMs": 500
  }
}
```

---

## Migration Checklist

When updating a template from old to new format:

- [ ] Add `navigation` object at root level
- [ ] Set `navigation.type` (Keyboard, RelativeClick, or Hybrid)
- [ ] Add `navigation.description` for documentation
- [ ] Configure `navigation.sequence` (for Keyboard/Hybrid types)
- [ ] Configure `navigation.clickOffset` (for RelativeClick/Hybrid fallback)
- [ ] Set `navigation.postNavigationDelayMs` (time for UI to respond)
- [ ] For Hybrid: Add `navigation.fallback` with RelativeClick configuration
- [ ] Update `version` to "2.0.0" to indicate new format
- [ ] Keep old `action` object for backward compatibility (optional)
- [ ] Test JSON validity (use JSON validator)

---

## Delay Recommendations

### Keyboard Action Delays
- **Tab/Arrow navigation:** 100-150ms between actions
- **Enter/Spacebar:** 500ms after activation (allow UI response)
- **Multiple rapid inputs:** 50-100ms between repetitions

### Post-Navigation Delays
- **Simple button click:** 500-1000ms
- **Screen transition:** 1000-2000ms
- **Login/authentication:** 2000-3000ms
- **Game launch:** 3000-5000ms

### General Rule
- **Faster is better**, but ensure UI has time to respond
- **Test and adjust** based on actual UI responsiveness
- **Slower systems may need longer delays**

---

## Testing Your JSON

### Validation Tools
```bash
# Use any JSON validator to check syntax
# Example online: https://jsonlint.com/

# Or use VS Code's built-in JSON validation
# Just open the .json file in VS Code
```

### Common Mistakes
1. **Missing commas** between properties
2. **Extra trailing commas** in arrays/objects
3. **Wrong quotes** (use double quotes `"` not single `'`)
4. **Invalid number format** (use 0.5 not "0.5" for numbers)
5. **Typos in action names** (use exact case from supported list)

### Validation Checklist
- [ ] JSON is valid (no syntax errors)
- [ ] All required fields present
- [ ] Action names match supported list
- [ ] Relative offsets are 0.0 to 1.0
- [ ] Delays are positive integers
- [ ] Counts are positive integers
- [ ] Type is one of: Keyboard, RelativeClick, Hybrid

---

## Need Help?

### When Keyboard Navigation Doesn't Work
1. Try increasing delays between actions
2. Verify the Tab order manually
3. Check if focus is on the correct window
4. Consider using Hybrid mode with click fallback

### When Click Navigation Is Off
1. Verify template detection is working (check confidence)
2. Adjust click offset values (try 0.5, 0.5 first)
3. Check if window is on primary monitor
4. Ensure template region matches clickable element

### When Both Methods Fail
1. Review logs for error messages
2. Verify window handle is valid
3. Check if UI has changed (new game version?)
4. Test with increased delays
5. Consider if element is actually clickable/navigable

---

## Advanced: Character Slot Navigation

For FFXI character slots, use arrow navigation:

```json
{
  "name": "ffxi_character_selection",
  "templatePath": "FFXI/character_selection.png",
  "confidenceThreshold": 0.85,
  "version": "2.0.0",
  "navigation": {
    "type": "Keyboard",
    "description": "Navigate to character slot using arrows",
    "sequence": [
      {
        "action": "DownArrow",
        "count": 2,
        "delayMs": 200,
        "description": "Navigate from slot 1 to slot 3"
      },
      {
        "action": "Enter",
        "count": 1,
        "delayMs": 3000,
        "description": "Select character and wait for loading"
      }
    ],
    "postNavigationDelayMs": 2000
  }
}
```

**Note:** For dynamic slot selection, the handler code will need to calculate
the `count` value based on user configuration. The template defines the base pattern.

---

_This is a living document. Update as you discover new navigation patterns._

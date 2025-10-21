# FFXIManager Workflows

This directory contains workflow definitions for the FFXIManager auto-login system.

## Directory Structure

**Source (application directory - flat structure):**
```
workflows/
├── playonline-standard.json  # Default workflow files (GUID-based filenames)
├── templates/                # Template PNG files (shared by all workflows)
│   └── *.png
└── README.md                 # This file
```

**User workflows (APPDATA - flat structure):**
```
%APPDATA%/FFXIManager/workflows/
├── {workflow-guid-1}.json   # Default workflow (copied from application directory)
├── {workflow-guid-2}.json   # Custom workflow
├── ...
└── templates/               # Template PNG files (shared by all workflows)
    └── *.png
```

## Using the Workflow Editor

### What is the Workflow Editor?

The **Workflow Editor** is a visual tool for creating and customizing auto-login workflows without editing JSON files manually. Think of workflows as "recipes" that tell FFXIManager exactly what buttons to press, where to click, and what screens to wait for during the login process.

**Opening the Workflow Editor:**
- From FFXIManager main window, navigate to **Settings → Workflow Editor**
- Or use the toolbar button if available

### Understanding the Interface

The Workflow Editor has **two main tabs**:

1. **Workflows Tab** - Manage your workflow collection
2. **Step Editor Tab** - Edit individual steps within a workflow

---

### Workflows Tab: Managing Your Workflow Collection

**Left Panel: Workflow List**
- Shows all available workflows (default and custom)
- **DEFAULT** badge = shipped with FFXIManager
- **READ-ONLY** badge = cannot be modified (clone it first!)
- Click a workflow to select it and view/edit its properties

**Right Panel: Workflow Properties**
- **Name**: Friendly name for your workflow (e.g., "PlayOnline Standard")
- **Description**: What this workflow does and when to use it
- **Version**: Semantic version (e.g., "1.0.0") - update when you make changes
- **Tags**: Keywords for filtering (comma-separated, e.g., "playonline, windower")
- **Is Default Workflow**: Mark this workflow as the default for new accounts
- **Read Only**: Lock the workflow to prevent accidental edits

**Action Buttons:**
- **Guide** - Opens this README file
- **💾 Save** (Ctrl+S) - Saves all changes to the selected workflow
- **✅ Validate** (F5) - Checks workflow for errors (missing templates, invalid steps)
- **🧪 Test** (F6) - Dry run to test the workflow without actually logging in
- **🗑️ Delete** - Permanently removes the selected workflow

**Bottom Action Bar:**
- **➕ Create New** - Start a new workflow from scratch
- **📋 Clone** - Duplicate the selected workflow for customization
- **📥 Import** - Load a workflow JSON file shared by the community
- **📤 Export** - Save your workflow to share with others
- **🔄 Restore Defaults** - Re-copy default workflows from the application directory

---

### Step Editor Tab: Building Your Login Sequence

When you select a workflow, the Step Editor shows all the steps that make up the login process.

**Left Panel: Step List**
- Shows all steps in execution order (0, 1, 2, ...)
- Each step shows:
  - **Order number** (execution sequence)
  - **Display Name** (e.g., "Member Selection")
  - **Description** (what the step does)
  - **Duration estimate** (~5s, ~30s)
  - **Status indicators** (✓ enabled, ○ optional)

**Right Panel: Step Properties (Two Sub-Tabs)**

#### Properties Tab

**Basic Information:**
- **Display Name**: Human-readable name (shown in logs and UI)
- **Description**: What this step accomplishes
- **Step ID**: Permanent identifier (🔒 cannot be changed once created)

**Detection & Timing:**
- **Template**: PNG image used to detect the screen
  - Click **📷 Select Image** to choose a screenshot
  - Click **🔄 Replace** to update the template image
  - Click the thumbnail preview to view full-size
  - For Click actions, click the preview to set click positions
- **Duration (seconds)**: How long this step typically takes (3-30s)
- **Detection Delay (ms)**: Time between screen detection attempts (500-1000ms)
- **Step Exec Retries**: How many times to retry if the step fails (2-3)
- **Detection Retries**: How many times to poll for template detection (30-120)
- **Skip If Running**: Skip this step if an application is already running (e.g., skip POL navigation if POL Proxy is running)

**Execution Settings:**
- **Enabled**: Uncheck to completely skip this step
- **Optional**: Check if step failure should not abort the entire workflow

#### Actions Tab

This is where you define **what happens** after a screen is detected.

**Actions Sequence (DataGrid):**
Each row is an action performed in order:
- **Type**: What action to perform (Tab, Enter, Click, InputPassword, etc.)
- **Description**: Human-readable description of what this action does
- **Repeat Count**: How many times to repeat the action
- **Delay After (ms)**: Wait time after this action completes

**Action Types Available:**
- **Keyboard Actions**: Tab, Enter, Escape, Up, Down, Left, Right
- **Special Input**: InputPassword, InputOTP (retrieves from secure storage)
- **Click Actions**: Click at specific coordinates on the template
- **Slot Navigation**: MemberSlot, CharacterSlot (handles multiple slots)
- **Application Launch**: Launch external applications
- **Wait**: Pause for a specified duration

**Action Properties Panel (Bottom):**
When you select an action from the grid, additional properties appear:

- **Retry Properties**: Fine-tune detection retries and delays per action
- **Template**: Override the step's template for this specific action
- **Confidence**: Template matching strictness (0.7-0.85 recommended)
- **Click Points**: For Click/MemberSlot/CharacterSlot actions, configure where to click

**Special Action Configuration:**

- **Launch Actions**:
  - Select which application to launch (from External Applications settings)
  - Configure skip options (skip if running, skip if not configured)

- **MemberSlot/CharacterSlot Actions**:
  - Click the template preview to set 4 click points (for slots 1-4)
  - The system automatically clicks the correct slot based on account settings

- **InputPassword/InputOTP**:
  - Credentials retrieved securely from Windows Credential Manager
  - Masked in logs for security

**Bottom Action Bar:**
- **➕ Add Action** - Insert a new action into the sequence
- **🗑️ Delete Action** - Remove the selected action
- **↑ Move Up** / **↓ Move Down** - Reorder actions in the sequence

---

### How the UI Maps to JSON

When you save a workflow, the Workflow Editor creates a JSON file that looks like this:

**Workflow Properties → JSON:**
```json
{
  "WorkflowId": "a8f5c3d2-e1b4-4a7f-9c8b-2d3e4f5a6b7c",
  "Name": "PlayOnline Standard",              ← From "Name" field
  "Description": "Complete workflow...",      ← From "Description" field
  "Version": "2.0.0",                         ← From "Version" field
  "IsDefault": true,                          ← From "Is Default Workflow" checkbox
  "IsReadOnly": false,                        ← From "Read Only" checkbox
  "Tags": ["playonline", "standard"],         ← From "Tags" field (comma-separated)
  "Steps": [ ... ]                            ← From Step Editor
}
```

**Step Properties → JSON:**
```json
{
  "StepId": "pol_member_selection",           ← From "Step ID" (immutable)
  "DisplayName": "Member Selection",          ← From "Display Name"
  "Description": "Select the member...",      ← From "Description"
  "Order": 2,                                 ← Position in step list
  "TemplatePath": "member_selection_screen",  ← From "Template" field
  "IsEnabled": true,                          ← From "Enabled" checkbox
  "IsOptional": false,                        ← From "Optional" checkbox
  "EstimatedDurationSeconds": 30,             ← From "Duration (seconds)"
  "MaxRetryAttempts": 3,                      ← From "Step Exec Retries"
  "RetryAttempts": 60,                        ← From "Detection Retries"
  "RetryDelayMs": 500,                        ← From "Detection Delay (ms)"
  "SkipIfApplicationRunning": "POL Proxy",    ← From "Skip If Running" dropdown
  "Navigation": { ... }                       ← From Actions tab
}
```

**Actions Sequence → JSON:**
```json
"Navigation": {
  "Description": "Navigate to member slot",   ← From "Navigation Description"
  "PostNavigationDelayMs": 1000,              ← From "Post-Sequence Delay (ms)"
  "Sequence": [
    {
      "Action": "Tab",                        ← From "Type" column
      "Count": 1,                             ← From "Repeat Count" column
      "DelayMs": 200,                         ← From "Delay After (ms)" column
      "Description": "Navigate to field",     ← From "Description" column
      "Parameters": { ... }                   ← From Action Properties panel
    }
  ]
}
```

**The workflow is automatically saved** to `%APPDATA%/FFXIManager/workflows/{workflow-guid}.json` when you click the **💾 Save** button.

---

### Common Workflows

#### Creating a Custom Workflow

**Scenario**: You want to create a workflow that skips OTP entry because your account doesn't use two-factor authentication.

1. **Clone the default workflow**:
   - Select "PlayOnline Standard" from the Workflows tab
   - Click **📋 Clone**
   - Rename it to "PlayOnline No OTP"

2. **Disable the OTP step**:
   - Switch to Step Editor tab
   - Find the step "OTP Entry" (Order 7)
   - In Properties tab, **uncheck "Enabled"**

3. **Save and test**:
   - Click **💾 Save** to save your changes
   - Click **✅ Validate** to check for errors
   - Assign this workflow to your account in Settings

#### Adjusting Click Positions for Your Resolution

**Scenario**: The workflow clicks the wrong button because your screen resolution is different.

1. **Select the problematic step**:
   - In Step Editor, find the step that's clicking incorrectly
   - Switch to **Actions** tab

2. **Update the click position**:
   - Select the Click action from the grid
   - In Action Properties panel, click the **template preview image**
   - Click where the button actually is on the preview
   - The click coordinates update automatically

3. **Save and test**:
   - Click **💾 Save**
   - Run the workflow to verify the fix

#### Adding a New Step

**Scenario**: You want to add a wait step after password entry to give the server more time to respond.

1. **Navigate to the step location**:
   - In Step Editor, select the step **after** which you want to insert the new step
   - Click **➕ Add Step** in the bottom action bar

2. **Configure the new step**:
   - **Display Name**: "Wait for Server Response"
   - **Description**: "Pause to allow server processing time"
   - **Template**: Leave empty (no screen detection needed)
   - **Duration**: 5 seconds
   - **Enabled**: ✓ checked

3. **Add the wait action**:
   - Switch to **Actions** tab
   - Click **Initialize Navigation** button
   - In the DataGrid, change Action Type to **Wait**
   - Set **Delay After (ms)** to **5000** (5 seconds)

4. **Save**:
   - Click **💾 Save**
   - The new step will execute in sequence

---

### Tips for Success

**When Working with Templates:**
- Use high-quality screenshots (avoid compression artifacts)
- Capture the same screen region consistently
- Test templates across different resolutions if possible
- Use **Confidence** slider to tune matching sensitivity (0.75-0.85 is good)

**When Working with Actions:**
- **Delays matter**: If navigation fails, try increasing delays
- **Test incrementally**: Add one action at a time and test
- **Use descriptions**: Future-you will thank you for clear descriptions

**When Working with Workflows:**
- **Clone before modifying defaults**: Keep originals as reference
- **Version your changes**: Update the version number when you make changes
- **Export your work**: Back up custom workflows periodically

---

## Default Workflows

Default workflows are:
- **Provided**: Shipped with FFXIManager in `workflows/` (application directory - flat structure)
- **Copied on startup**: Automatically copied to APPDATA using GUID-based filenames
- **Restorable**: Users can restore defaults if they make mistakes
- **Cloneable**: Users can clone them to create custom variations
- **Modifiable**: Users can modify default workflows freely

**Flat Structure Benefits:**
- Simple file organization - all workflows at root level
- No nested subdirectories for easier navigation
- Templates shared in `templates/` subfolder

On application startup, default workflows are copied from:
```
<AppDir>/workflows/*.json → %APPDATA%/FFXIManager/workflows/{workflow-guid}.json
<AppDir>/workflows/templates/*.png → %APPDATA%/FFXIManager/workflows/templates/*.png
```

## Workflow File Format

Workflows are defined as JSON files with the following structure:

```json
{
  "WorkflowId": "unique-guid-here",
  "Name": "Workflow Name",
  "Description": "Detailed description of what this workflow does",
  "Version": "1.0.0",
  "IsDefault": false,
  "IsReadOnly": false,
  "CreatedDate": "2025-01-15T00:00:00Z",
  "LastModifiedDate": "2025-01-15T00:00:00Z",
  "Author": "Your Name",
  "Tags": ["tag1", "tag2"],
  "Steps": [
    {
      "StepId": "step_identifier",
      "DisplayName": "Step Display Name",
      "Description": "What this step does",
      "Order": 0,
      "TemplatePath": "Application/template_name",
      "IsEnabled": true,
      "IsOptional": false,
      "EstimatedDurationSeconds": 5,
      "MaxRetryAttempts": 3,
      "Navigation": {
        "Description": "Navigation description",
        "PostNavigationDelayMs": 500,
        "Sequence": [
          {
            "Action": "Tab",
            "Count": 1,
            "DelayMs": 200,
            "Description": "Navigate to field"
          },
          {
            "Action": "Enter",
            "Count": 1,
            "DelayMs": 100,
            "Description": "Submit"
          }
        ]
      }
    }
  ],
  "Metadata": {
    "CustomKey": "CustomValue"
  }
}
```

## Navigation Actions

### Keyboard Actions
- `Tab` - Press Tab key to navigate
- `Enter` - Press Enter key
- `Escape` - Press Escape key
- `Up` / `Down` / `Left` / `Right` - Arrow keys
- `Space` - Press spacebar
- `TypePassword` - Type the account password (special action)
- `TypeOTP` - Type the OTP code (special action)

### Click Actions
```json
{
  "Action": "Click",
  "ClickX": 0.5,
  "ClickY": 0.3,
  "DelayMs": 100,
  "Description": "Click on button"
}
```
- `ClickX` and `ClickY` are relative coordinates (0.0 to 1.0)
- `0.5, 0.5` represents the center of the window

## Creating Custom Workflows

1. **Clone a Default Workflow**:
   - Open FFXIManager
   - Go to Workflow Editor
   - Select a default workflow
   - Click "Clone"
   - Customize the cloned workflow

2. **Or Create from Scratch**:
   - Click "Create New Workflow"
   - Add steps one by one
   - Define navigation sequences
   - Attach templates for screen detection
   - Save your workflow

3. **Export/Import**:
   - Export: Save your custom workflow to share with others
   - Import: Load workflows created by the community

## Template Paths

Template paths reference PNG filenames without extension from the shared templates directory.

Examples:
- `member_selection_screen`
- `password_entry_screen`
- `character_selection_screen`

Templates are stored in:
```
%APPDATA%/FFXIManager/workflows/templates/
```

Templates are shared by all workflows (default and custom) in a centralized location.

Note: Template JSON metadata files are no longer used. All detection thresholds, tolerances, retries, and navigation are defined in workflows (WorkflowDefinition/WorkflowStepDefinition and per-action Parameters). Only PNG images are required in the templates folder.

## Best Practices

1. **Version Your Workflows**: Update the `Version` field when making changes
2. **Use Descriptive Names**: Make it clear what the workflow accomplishes
3. **Add Tags**: Use tags for easy filtering and categorization
4. **Test Thoroughly**: Verify workflows work across different scenarios
5. **Set Realistic Durations**: Estimate `EstimatedDurationSeconds` accurately
6. **Keep Steps Atomic**: Each step should do one thing well

## Field Semantics (Workflow-First)

- `TemplatePath`: Template PNG filename under `%APPDATA%/FFXIManager/workflows/templates` (flat). Use `name` or `name.png`.
- `EstimatedDurationSeconds`: Sizes detection timeouts and progress; not a hard per-step cap. Global TTL is `SubtaskTimeoutSeconds`.
- `MaxRetryAttempts`: Step-level retry budget if the subtask fails (executor-level), not per detection attempt.
- `RetryAttempts` / `RetryDelayMs`: Detection wait tuning for screen readiness (step-level default; action-level can override).
- `ConfidenceThreshold` / `Tolerance`: Matching thresholds (step-level defaults; action-level can override).
- `IsOptional`: If the step fails, it will be skipped and will not abort the workflow.
- `SkipIfApplicationRunning`: Evaluated once at task-build time using a snapshot of running apps.

## Troubleshooting

- **Workflow Not Loading**: Check JSON syntax with a validator
- **Step Not Executing**: Verify `IsEnabled` is true
- **Navigation Fails**: Check delays between actions (increase if needed)
- **Template Not Found**: Verify template path and ensure template exists

## Version History

- **1.0.0** (2025-01-15): Initial workflow system release

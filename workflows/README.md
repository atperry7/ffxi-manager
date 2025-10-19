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

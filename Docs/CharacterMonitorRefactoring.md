# Character Monitor Refactoring Documentation

## Overview
The Character Monitor has been completely refactored with a clean, modular architecture that separates concerns and makes future enhancements much easier.

## New Architecture

### Core Components

#### 1. **CharacterMonitorViewModel** (Main Coordinator)
- Location: `ViewModels/CharacterMonitor/CharacterMonitorViewModel.cs`
- Purpose: Top-level ViewModel that coordinates window and collection ViewModels
- Responsibilities:
  - Creates and manages sub-ViewModels
  - Handles window close requests
  - Coordinates auto-hide behavior

#### 2. **CharacterMonitorWindowViewModel** (Window State)
- Location: `ViewModels/CharacterMonitor/CharacterMonitorWindowViewModel.cs`
- Purpose: Manages all window-specific UI state
- Responsibilities:
  - Window sizing and positioning
  - Opacity and visual settings
  - Always-on-top and auto-hide states
  - Docking positions
  - Menu commands

#### 3. **CharacterCollectionViewModel** (Character Management)
- Location: `ViewModels/CharacterMonitor/CharacterCollectionViewModel.cs`
- Purpose: Manages the collection of characters
- Responsibilities:
  - Character list management
  - Activation logic
  - Ordering/reordering
  - Performance metrics
  - Service integration

#### 4. **CharacterItemViewModel** (Individual Characters)
- Location: `ViewModels/CharacterMonitor/CharacterItemViewModel.cs`
- Purpose: Wraps each character with UI-specific properties
- Responsibilities:
  - Character display properties
  - Activation state
  - Visual feedback (borders, colors)
  - Status text formatting

#### 5. **CharacterMonitorWindowV2** (View)
- Location: `Views/CharacterMonitorWindowV2.xaml(.cs)`
- Purpose: Clean XAML view with minimal code-behind
- Features:
  - Pure MVVM data binding
  - Responsive layout
  - Clean separation from logic

## Migration Path

### Using the New Implementation

The new Character Monitor can be accessed through the helper class:

```csharp
// Show the new Character Monitor window
CharacterMonitorHelper.ShowCharacterMonitor();

// Check if window is open
bool isOpen = CharacterMonitorHelper.IsCharacterMonitorOpen;

// Close the window programmatically
CharacterMonitorHelper.CloseCharacterMonitor();
```

### Transition Steps

1. **Current State**: The `PlayOnlineMonitorViewModel` has been updated to use the new implementation via `CharacterMonitorHelper`.

2. **Testing Phase**: Both implementations exist side-by-side:
   - Old: `CharacterMonitorWindow` + `PlayOnlineMonitorViewModel`
   - New: `CharacterMonitorWindowV2` + new ViewModels

3. **Cleanup Phase** (When ready):
   - Remove `Views/CharacterMonitorWindow.xaml(.cs)`
   - Remove character-specific logic from `PlayOnlineMonitorViewModel`
   - Rename `CharacterMonitorWindowV2` to `CharacterMonitorWindow`

## Key Improvements

### 1. **Separation of Concerns**
- Window UI state is completely separate from character data
- Character monitoring logic is decoupled from UI
- Each ViewModel has a single, clear responsibility

### 2. **Better State Management**
- Centralized state updates through ViewModels
- Proper event handling with weak references
- Thread-safe UI updates

### 3. **Enhanced Maintainability**
- Clear component boundaries
- Easy to add new features without breaking existing ones
- Testable ViewModels

### 4. **Improved Performance**
- Efficient collection updates
- Proper disposal of resources
- Optimized event handling

## Architecture Diagram

```
┌─────────────────────────────────────┐
│    CharacterMonitorWindowV2 (View)  │
│         - Minimal code-behind        │
│         - Pure XAML binding          │
└────────────────┬────────────────────┘
                 │ DataContext
                 ▼
┌─────────────────────────────────────┐
│  CharacterMonitorViewModel (Main)   │
│      - Coordinates sub-VMs          │
│      - Handles window lifecycle     │
└──────┬─────────────────┬────────────┘
       │                 │
       ▼                 ▼
┌──────────────┐  ┌───────────────────┐
│ WindowVM     │  │ CollectionVM      │
│ - UI State   │  │ - Characters      │
│ - Settings   │  │ - Activation      │
│ - Commands   │  │ - Ordering        │
└──────────────┘  └─────────┬─────────┘
                            │
                            ▼
                  ┌─────────────────┐
                  │ CharacterItemVM │
                  │ - Display props │
                  │ - Status        │
                  │ - Commands      │
                  └─────────────────┘
```

## Testing the New Implementation

1. **Build the project**: `dotnet build`
2. **Run the application**: `dotnet run`
3. **Open Character Monitor**: Use the menu or hotkey to open the monitor
4. **Test features**:
   - Character switching
   - Reordering characters
   - Window resizing/docking
   - Opacity adjustment
   - Auto-hide functionality
   - Click-to-switch mode

## Next Steps

After confirming the new implementation works correctly:

1. **Monitor for issues** during regular use
2. **Gather feedback** on the improved architecture
3. **Remove old code** when confident in stability
4. **Add new features** leveraging the clean architecture

## Benefits for Future Development

With this new architecture, adding features becomes much simpler:

- **New UI features**: Add to `CharacterMonitorWindowViewModel`
- **Character operations**: Add to `CharacterCollectionViewModel`
- **Character properties**: Add to `CharacterItemViewModel`
- **Window behaviors**: Update `CharacterMonitorWindowV2` XAML

Each change is isolated and won't cascade through the entire codebase.
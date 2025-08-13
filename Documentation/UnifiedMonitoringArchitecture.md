# Unified Monitoring Architecture

## Overview
The FFXIManager now uses a centralized, unified monitoring service that provides consistent process detection and tracking for all components. This replaces the previous approach where each service had its own monitoring implementation.

## Architecture Components

### 1. UnifiedMonitoringService (Core)
- **Location**: `Services/UnifiedMonitoringService.cs`
- **Purpose**: Central hub for all process monitoring
- **Features**:
  - Single WMI/Discovery watch registration
  - Filtered event distribution to consumers
  - Periodic safety scans (10-second intervals)
  - Window title tracking when needed
  - Context data support for consumer-specific information

### 2. ProcessManagementService (Foundation)
- **Location**: `Infrastructure/ProcessManagementService.cs`
- **Purpose**: Low-level Windows API and WMI interactions
- **Features**:
  - WMI process creation/termination detection
  - Window enumeration and title tracking
  - Process killing and window activation
  - Discovery watch management

### 3. Consumer Services

#### PlayOnlineMonitorServiceV2
- **Location**: `Services/PlayOnlineMonitorServiceV2.cs`
- **Purpose**: Track PlayOnline/FFXI instances
- **Profile Settings**:
  - ProcessNames: `["pol", "ffxi", "PlayOnlineViewer"]`
  - TrackWindows: `true`
  - TrackWindowTitles: `true`
  - IncludeProcessPath: `false`

#### ExternalApplicationServiceV2
- **Location**: `Services/ExternalApplicationServiceV2.cs`
- **Purpose**: Manage and track external applications
- **Profile Settings**:
  - ProcessNames: Dynamic based on configured apps
  - TrackWindows: `false`
  - TrackWindowTitles: `false`
  - IncludeProcessPath: `true`

## Key Benefits

### 1. **Consistency**
- All services receive the same quality of process detection
- No more timing differences between monitors
- Unified event flow ensures predictable behavior

### 2. **Performance**
- Single set of WMI watchers for all monitoring
- Shared discovery watches reduce system overhead
- Efficient filtering prevents unnecessary processing

### 3. **Maintainability**
- Bug fixes in one place benefit all consumers
- Easy to add new monitoring profiles
- Clear separation of concerns

### 4. **Reliability**
- Periodic safety scans catch any missed events
- Initial scan ensures existing processes are detected
- Proper thread synchronization prevents race conditions

## Event Flow

```
1. Process Starts
   ↓
2. WMI Detection → ProcessManagementService
   ↓
3. ProcessDetected Event → UnifiedMonitoringService
   ↓
4. Profile Matching & Filtering
   ↓
5. Consumer-Specific Event (filtered by MonitorId)
   ↓
6. UI Update via Consumer Service
```

## Migration from Old Services

### Old Architecture Issues
- **PlayOnlineMonitorService**: Complex retry logic, window enumeration delays
- **ExternalApplicationService**: Duplicate monitoring logic
- **Timing Issues**: Different detection speeds for different services
- **Race Conditions**: Multiple services fighting for process information

### New Architecture Solutions
- **Immediate Detection**: All processes detected instantly
- **Shared Infrastructure**: One monitoring system for all
- **Clean Abstractions**: Each service only handles its specific logic
- **Professional Design**: Follows SOLID principles and best practices

## Adding New Monitors

To add a new monitoring consumer:

1. Create a monitoring profile:
```csharp
var profile = new MonitoringProfile
{
    Name = "My Monitor",
    ProcessNames = new[] { "target.exe" },
    TrackWindows = true,
    TrackWindowTitles = false,
    IncludeProcessPath = false
};
```

2. Register with UnifiedMonitoringService:
```csharp
var monitorId = unifiedMonitoring.RegisterMonitor(profile);
```

3. Subscribe to events:
```csharp
unifiedMonitoring.ProcessDetected += OnProcessDetected;
unifiedMonitoring.ProcessUpdated += OnProcessUpdated;
unifiedMonitoring.ProcessRemoved += OnProcessRemoved;
```

4. Filter by your MonitorId in event handlers:
```csharp
private void OnProcessDetected(object? sender, MonitoredProcessEventArgs e)
{
    if (e.MonitorId != _monitorId) return;
    // Handle your process
}
```

## Configuration

The unified monitoring system starts automatically when any consumer service starts monitoring. It runs with:
- **Global Monitoring Interval**: 3 seconds
- **Periodic Safety Scan**: 10 seconds
- **WMI Event Interval**: 1 second

## Testing

The architecture supports easy testing through:
- ServiceLocator injection for mock services
- Clean interfaces for all components
- Event-driven design for predictable testing

## Future Enhancements

Possible future improvements:
- Process CPU/Memory monitoring
- Network connection tracking
- Child process relationship tracking
- Performance metrics collection
- Historical process data storage

## Conclusion

The unified monitoring architecture provides a professional, maintainable, and efficient solution for all process monitoring needs in FFXIManager. It eliminates code duplication, ensures consistent behavior, and provides a solid foundation for future enhancements.

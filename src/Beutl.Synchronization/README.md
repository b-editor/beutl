# Beutl.Synchronization

Real-time CoreObject synchronization library for Beutl. This library enables collaborative editing by synchronizing property changes across multiple clients in real-time.

## Features

- **Automatic Synchronization**: CoreObject property changes are automatically synchronized
- **Hierarchical Support**: Full support for Project → Scene → Element hierarchies
- **Real-time Updates**: Changes are propagated instantly to connected clients
- **Conflict Resolution**: Sequence-based ordering prevents conflicts
- **Transport Abstraction**: Pluggable transport layer (Memory, SignalR, WebSocket, etc.)
- **Type Safety**: Strongly typed with proper serialization/deserialization

## Quick Start

### Basic Usage

```csharp
using Beutl.Synchronization;
using Beutl.Synchronization.Extensions;

// Create synchronization components
var (syncManager, orchestrator, sourceId) = SyncConfigurationExtensions
    .ConfigureSync()
    .UseMemoryTransport()
    .WithSourceId("Client1")
    .Build();

// Start synchronization session
var sessionId = Guid.NewGuid();
await syncManager.StartSyncAsync(sessionId);

// Synchronize a project
await orchestrator.SyncProjectAsync(project, sourceId);

// Now all property changes are automatically synchronized!
project.Name = "Collaborative Project"; // This change will be sent to other clients
```

### Manual Object Synchronization

```csharp
// Enable sync for individual objects
var element = new SomeElement();
element.EnableSync(syncManager, "Client1");

// Changes are now synchronized
element.SomeProperty = "New Value"; // Automatically sent to other clients

// Disable sync when done
element.DisableSync();
```

### Receiving Remote Changes

```csharp
// Subscribe to remote changes
syncManager.RemoteChanges.Subscribe(change =>
{
    Console.WriteLine($"Remote change: {change.ObjectId}.{change.PropertyName} = {change.NewValue}");
});

// Subscribe to local changes being sent
syncManager.LocalChanges.Subscribe(change =>
{
    Console.WriteLine($"Sent change: {change.ObjectId}.{change.PropertyName} = {change.NewValue}");
});
```

## Architecture

### Core Components

1. **ISyncManager**: Main synchronization coordinator
2. **ISyncTransport**: Abstract transport layer
3. **ProjectSyncOrchestrator**: High-level project synchronization
4. **CoreObjectSyncExtensions**: Extension methods for CoreObject sync

### Transport Layer

The library uses an abstract transport layer that can be implemented for different communication methods:

- **MemoryTransport**: In-memory transport for testing and local sync
- **SignalRTransport**: (Planned) Real-time web communication
- **WebSocketTransport**: (Planned) Direct WebSocket communication

### Data Flow

```
CoreObject Property Change
    ↓
CoreObjectSyncExtensions (capture change)
    ↓
ChangeNotification (serialize)
    ↓
SyncManager (coordination)
    ↓
ISyncTransport (send to remote)
    ↓
Remote Clients (receive and apply)
```

## Configuration Options

### Memory Transport (Testing)

```csharp
var syncManager = SyncFactory.CreateMemorySyncManager();
```

### SignalR Transport (Planned)

```csharp
var config = new SyncTransportConfig
{
    ServerUrl = "https://your-server.com/synchub",
    AuthToken = "your-auth-token"
};
var syncManager = SyncFactory.CreateSignalRSyncManager(config);
```

### Builder Pattern

```csharp
var (syncManager, orchestrator, sourceId) = SyncConfigurationExtensions
    .ConfigureSync()
    .UseMemoryTransport()
    .WithLogging(loggerFactory)
    .WithSourceId("ClientName")
    .Build();
```

## Advanced Usage

### Custom Change Filtering

```csharp
// Filter which changes to sync
syncManager.LocalChanges
    .Where(change => change.PropertyName != "InternalProperty")
    .Subscribe(change => /* handle filtered changes */);
```

### Batch Operations

```csharp
// Temporarily disable sync for batch operations
element.DisableSync();
try
{
    // Make multiple changes
    element.Property1 = value1;
    element.Property2 = value2;
    element.Property3 = value3;
}
finally
{
    element.EnableSync(syncManager, sourceId);
}
```

### Connection Management

```csharp
// Monitor connection status
syncManager.ConnectionStatusChanged.Subscribe(status =>
{
    Console.WriteLine($"Connection status: {status}");
});

// Handle disconnections
if (syncManager.ConnectionStatus == SyncConnectionStatus.Disconnected)
{
    await syncManager.StartSyncAsync(sessionId);
}
```

## Thread Safety

All synchronization operations are thread-safe. Property changes can occur on any thread and will be properly synchronized. The library uses Reactive Extensions (Rx) for managing asynchronous operations.

## Performance Considerations

- Changes are serialized using System.Text.Json for efficiency
- Sequence numbers prevent duplicate processing
- Weak references prevent memory leaks
- Background threads handle network operations

## Error Handling

The library includes comprehensive error handling and logging:

```csharp
// Configure logging
var loggerFactory = LoggerFactory.Create(builder => 
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

var (syncManager, orchestrator, sourceId) = SyncConfigurationExtensions
    .ConfigureSync()
    .UseMemoryTransport()
    .WithLogging(loggerFactory)
    .Build();
```

## Limitations

- Currently supports property-level synchronization only
- Complex object references may require manual handling
- Network partitions require application-level recovery logic

## Future Enhancements

- SignalR transport implementation
- WebSocket transport implementation
- Conflict resolution strategies
- Offline synchronization support
- Authentication and authorization
- Performance optimizations
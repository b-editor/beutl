using System.ComponentModel;
using System.Reactive.Linq;
using System.Text.Json;
using Beutl.Synchronization.Core;
using Microsoft.Extensions.Logging;

namespace Beutl.Synchronization.Extensions;

/// <summary>
/// Extension methods for enabling CoreObject synchronization
/// </summary>
public static class CoreObjectSyncExtensions
{
    private static readonly ILogger _logger = CreateLogger();

    /// <summary>
    /// Enable synchronization for a CoreObject
    /// </summary>
    /// <param name="obj">CoreObject to synchronize</param>
    /// <param name="syncManager">Sync manager to use</param>
    /// <param name="sourceId">Source identifier for changes</param>
    public static void EnableSync(this CoreObject obj, ISyncManager syncManager, string? sourceId = null)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        if (syncManager == null) throw new ArgumentNullException(nameof(syncManager));

        var state = SyncStateRegistry.GetOrCreate(obj);
        
        // Prevent duplicate registration
        if (state.IsEnabled && state.SyncManager == syncManager)
        {
            _logger.LogDebug("Object {ObjectId} is already synchronized", obj.Id);
            return;
        }

        // Clean up existing sync if switching managers
        if (state.IsEnabled)
        {
            obj.DisableSync();
        }

        state.SyncManager = syncManager;
        state.IsEnabled = true;

        var changeSourceId = sourceId ?? ChangeSource.LocalClient;

        // Subscribe to local property changes
        state.PropertyChangeSubscription = Observable
            .FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                handler => obj.PropertyChanged += handler,
                handler => obj.PropertyChanged -= handler)
            .Where(evt => evt.EventArgs is CorePropertyChangedEventArgs)
            .Select(evt => evt.EventArgs as CorePropertyChangedEventArgs)
            .Where(args => args != null && !state.IsReceivingRemoteChange)
            .Subscribe(args => OnLocalPropertyChanged(obj, args!, syncManager, changeSourceId, state));

        // Subscribe to remote changes for this object
        state.RemoteChangeSubscription = syncManager.RemoteChanges
            .Where(change => change.ObjectId == obj.Id)
            .Subscribe(change => OnRemotePropertyChanged(obj, change, state));

        // Register with sync manager
        syncManager.RegisterObject(obj);

        _logger.LogDebug("Enabled synchronization for object {ObjectId} of type {ObjectType}", 
            obj.Id, obj.GetType().Name);
    }

    /// <summary>
    /// Disable synchronization for a CoreObject
    /// </summary>
    /// <param name="obj">CoreObject to stop synchronizing</param>
    public static void DisableSync(this CoreObject obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));

        if (!SyncStateRegistry.TryGetState(obj, out var state) || !state.IsEnabled)
        {
            return;
        }

        // Cleanup subscriptions
        state.PropertyChangeSubscription?.Dispose();
        state.RemoteChangeSubscription?.Dispose();

        // Unregister from sync manager
        state.SyncManager?.UnregisterObject(obj);

        // Reset state
        state.IsEnabled = false;
        state.SyncManager = null;

        _logger.LogDebug("Disabled synchronization for object {ObjectId}", obj.Id);
    }

    /// <summary>
    /// Check if a CoreObject is synchronized
    /// </summary>
    /// <param name="obj">CoreObject to check</param>
    /// <returns>True if synchronized</returns>
    public static bool IsSyncEnabled(this CoreObject obj)
    {
        if (obj == null) return false;
        
        return SyncStateRegistry.TryGetState(obj, out var state) && state.IsEnabled;
    }

    /// <summary>
    /// Get the sync manager for a CoreObject
    /// </summary>
    /// <param name="obj">CoreObject</param>
    /// <returns>Sync manager or null if not synchronized</returns>
    public static ISyncManager? GetSyncManager(this CoreObject obj)
    {
        if (obj == null) return null;
        
        return SyncStateRegistry.TryGetState(obj, out var state) ? state.SyncManager : null;
    }

    private static void OnLocalPropertyChanged(
        CoreObject obj,
        CorePropertyChangedEventArgs args,
        ISyncManager syncManager,
        string sourceId,
        SyncState state)
    {
        try
        {
            var change = new ChangeNotification
            {
                ObjectId = obj.Id,
                PropertyName = args.Property.Name,
                NewValue = SerializeValue(args.NewValue),
                OldValue = SerializeValue(args.OldValue),
                Timestamp = DateTime.UtcNow,
                ChangeSource = sourceId,
                SessionId = syncManager.SessionId,
                SequenceNumber = ++state.LocalSequenceNumber,
                ObjectTypeName = obj.GetType().AssemblyQualifiedName
            };

            // Send change asynchronously without blocking UI
            _ = Task.Run(async () =>
            {
                try
                {
                    await syncManager.SendChangeAsync(change);
                    _logger.LogTrace("Sent change for {ObjectId}.{PropertyName}", obj.Id, args.Property.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send change for {ObjectId}.{PropertyName}", 
                        obj.Id, args.Property.Name);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing local property change for {ObjectId}.{PropertyName}",
                obj.Id, args.Property.Name);
        }
    }

    private static void OnRemotePropertyChanged(CoreObject obj, ChangeNotification change, SyncState state)
    {
        try
        {
            // Prevent infinite loops
            if (state.IsReceivingRemoteChange)
            {
                _logger.LogTrace("Skipping remote change for {ObjectId}.{PropertyName} (already receiving)",
                    change.ObjectId, change.PropertyName);
                return;
            }

            // Check sequence number to prevent out-of-order updates
            if (change.SequenceNumber.HasValue && 
                change.SequenceNumber <= state.LastRemoteSequenceNumber)
            {
                _logger.LogTrace("Skipping out-of-order change for {ObjectId}.{PropertyName} " +
                                "(seq: {SequenceNumber}, last: {LastSequenceNumber})",
                    change.ObjectId, change.PropertyName, 
                    change.SequenceNumber, state.LastRemoteSequenceNumber);
                return;
            }

            state.IsReceivingRemoteChange = true;

            try
            {
                ApplyRemoteChange(obj, change);
                
                if (change.SequenceNumber.HasValue)
                {
                    state.LastRemoteSequenceNumber = change.SequenceNumber.Value;
                }

                _logger.LogTrace("Applied remote change for {ObjectId}.{PropertyName}",
                    change.ObjectId, change.PropertyName);
            }
            finally
            {
                state.IsReceivingRemoteChange = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying remote property change for {ObjectId}.{PropertyName}",
                change.ObjectId, change.PropertyName);
        }
    }

    private static void ApplyRemoteChange(CoreObject obj, ChangeNotification change)
    {
        // Find the property by name
        var properties = PropertyRegistry.GetRegistered(obj.GetType());
        var property = properties.FirstOrDefault(p => p.Name == change.PropertyName);

        if (property == null)
        {
            _logger.LogWarning("Property {PropertyName} not found on object {ObjectId} of type {ObjectType}",
                change.PropertyName, change.ObjectId, obj.GetType().Name);
            return;
        }

        try
        {
            // Deserialize and set the new value
            var deserializedValue = DeserializeValue(change.NewValue, property.PropertyType);
            obj.SetValue(property, deserializedValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply value {NewValue} to property {PropertyName} on object {ObjectId}",
                change.NewValue, change.PropertyName, change.ObjectId);
        }
    }

    private static object? SerializeValue(object? value)
    {
        if (value == null) return null;

        try
        {
            // For primitive types, return as-is
            if (value.GetType().IsPrimitive || value is string || value is DateTime || value is Guid)
            {
                return value;
            }

            // For complex types, serialize to JSON
            return JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to serialize value of type {ValueType}, using string representation",
                value.GetType().Name);
            return value.ToString();
        }
    }

    private static object? DeserializeValue(object? serializedValue, Type targetType)
    {
        if (serializedValue == null) return null;

        try
        {
            // For primitive types and strings, try direct conversion
            if (targetType.IsPrimitive || targetType == typeof(string) || 
                targetType == typeof(DateTime) || targetType == typeof(Guid))
            {
                return Convert.ChangeType(serializedValue, targetType);
            }

            // For complex types, deserialize from JSON
            if (serializedValue is string jsonString)
            {
                return JsonSerializer.Deserialize(jsonString, targetType);
            }

            // If it's already the correct type, return as-is
            if (targetType.IsAssignableFrom(serializedValue.GetType()))
            {
                return serializedValue;
            }

            // Try conversion as last resort
            return Convert.ChangeType(serializedValue, targetType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize value {SerializedValue} to type {TargetType}",
                serializedValue, targetType.Name);
            return null;
        }
    }

    private static ILogger CreateLogger()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        return loggerFactory.CreateLogger(typeof(CoreObjectSyncExtensions));
    }
}
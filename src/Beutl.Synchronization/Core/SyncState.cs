using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Beutl.Synchronization.Core;

/// <summary>
/// Internal state management for synchronized CoreObjects
/// </summary>
internal class SyncState
{
    /// <summary>
    /// Sync manager reference
    /// </summary>
    public ISyncManager? SyncManager { get; set; }

    /// <summary>
    /// Whether synchronization is enabled for this object
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether we're currently receiving a remote change (prevents echo)
    /// </summary>
    public bool IsReceivingRemoteChange { get; set; }

    /// <summary>
    /// Sequence number for ordering local changes
    /// </summary>
    public long LocalSequenceNumber { get; set; }

    /// <summary>
    /// Last known remote sequence number
    /// </summary>
    public long LastRemoteSequenceNumber { get; set; }

    /// <summary>
    /// Disposable for property change subscription
    /// </summary>
    public IDisposable? PropertyChangeSubscription { get; set; }

    /// <summary>
    /// Disposable for remote change subscription
    /// </summary>
    public IDisposable? RemoteChangeSubscription { get; set; }
}

/// <summary>
/// Global registry for tracking synchronized CoreObjects
/// </summary>
internal static class SyncStateRegistry
{
    private static readonly ConditionalWeakTable<CoreObject, SyncState> _syncStates = new();
    private static readonly ConcurrentDictionary<Guid, WeakReference<CoreObject>> _objectLookup = new();

    /// <summary>
    /// Get or create sync state for a CoreObject
    /// </summary>
    public static SyncState GetOrCreate(CoreObject obj)
    {
        var state = _syncStates.GetOrCreateValue(obj);
        
        // Register in lookup table for remote change resolution
        _objectLookup.AddOrUpdate(obj.Id, new WeakReference<CoreObject>(obj), 
            (key, existing) => new WeakReference<CoreObject>(obj));
        
        return state;
    }

    /// <summary>
    /// Try to get sync state for a CoreObject
    /// </summary>
    public static bool TryGetState(CoreObject obj, out SyncState? state)
    {
        return _syncStates.TryGetValue(obj, out state);
    }

    /// <summary>
    /// Try to find a CoreObject by its ID
    /// </summary>
    public static bool TryFindObject(Guid objectId, out CoreObject? obj)
    {
        obj = null;
        
        if (!_objectLookup.TryGetValue(objectId, out var weakRef))
            return false;

        if (!weakRef.TryGetTarget(out obj))
        {
            // Clean up dead reference
            _objectLookup.TryRemove(objectId, out _);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Remove a CoreObject from tracking
    /// </summary>
    public static void Remove(CoreObject obj)
    {
        if (_syncStates.TryGetValue(obj, out var state))
        {
            state.PropertyChangeSubscription?.Dispose();
            state.RemoteChangeSubscription?.Dispose();
        }
        
        _syncStates.Remove(obj);
        _objectLookup.TryRemove(obj.Id, out _);
    }

    /// <summary>
    /// Clean up dead weak references periodically
    /// </summary>
    public static void CleanupDeadReferences()
    {
        var keysToRemove = new List<Guid>();
        
        foreach (var kvp in _objectLookup)
        {
            if (!kvp.Value.TryGetTarget(out _))
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            _objectLookup.TryRemove(key, out _);
        }
    }
}
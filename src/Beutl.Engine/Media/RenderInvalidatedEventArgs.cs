using System.Collections;

namespace Beutl.Media;

/// <summary>
/// Event arguments for render invalidation events.
/// Supports object pooling to reduce GC pressure during animations.
/// </summary>
public class RenderInvalidatedEventArgs : EventArgs
{
    private object? _sender;
    private string? _propertyName;
    private RenderInvalidatedReason _reason;

    public RenderInvalidatedEventArgs()
    {
        // Parameterless constructor for object pooling
    }

    public RenderInvalidatedEventArgs(object? obj)
    {
        Initialize(obj, null, RenderInvalidatedReason.None);
    }

    public RenderInvalidatedEventArgs(ICollection collection)
    {
        Initialize(collection, null, RenderInvalidatedReason.CollectionChanged);
    }

    public RenderInvalidatedEventArgs(object? sender, string propertyName)
    {
        Initialize(sender, propertyName, RenderInvalidatedReason.PropertyChanged);
    }

    public object? Sender => _sender;

    public string? PropertyName => _propertyName;

    public RenderInvalidatedReason Reason => _reason;

    /// <summary>
    /// Initializes the event args with the specified values.
    /// Used when retrieving from object pool.
    /// </summary>
    internal void Initialize(object? sender, string? propertyName, RenderInvalidatedReason reason)
    {
        _sender = sender;
        _propertyName = propertyName;
        _reason = reason;
    }

    /// <summary>
    /// Resets the event args for return to object pool.
    /// </summary>
    internal void Reset()
    {
        _sender = null;
        _propertyName = null;
        _reason = RenderInvalidatedReason.None;
    }

    /// <summary>
    /// Creates a pooled RenderInvalidatedEventArgs instance.
    /// This reduces allocations during frequent invalidation events.
    /// </summary>
    public static RenderInvalidatedEventArgs GetPooled(object? sender)
    {
        var args = ObjectPools.RenderInvalidatedEventArgs.Get();
        args.Initialize(sender, null, RenderInvalidatedReason.None);
        return args;
    }

    /// <summary>
    /// Creates a pooled RenderInvalidatedEventArgs instance for collection changes.
    /// </summary>
    public static RenderInvalidatedEventArgs GetPooled(ICollection collection)
    {
        var args = ObjectPools.RenderInvalidatedEventArgs.Get();
        args.Initialize(collection, null, RenderInvalidatedReason.CollectionChanged);
        return args;
    }

    /// <summary>
    /// Creates a pooled RenderInvalidatedEventArgs instance for property changes.
    /// </summary>
    public static RenderInvalidatedEventArgs GetPooled(object? sender, string propertyName)
    {
        var args = ObjectPools.RenderInvalidatedEventArgs.Get();
        args.Initialize(sender, propertyName, RenderInvalidatedReason.PropertyChanged);
        return args;
    }

    /// <summary>
    /// Returns this instance to the object pool.
    /// Should be called after the event has been processed.
    /// </summary>
    public void ReturnToPool()
    {
        ObjectPools.RenderInvalidatedEventArgs.Return(this);
    }
}

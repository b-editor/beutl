using Microsoft.Extensions.ObjectPool;

namespace Beutl.Media;

/// <summary>
/// Provides object pooling for frequently allocated types to reduce GC pressure.
/// </summary>
public static class ObjectPools
{
    /// <summary>
    /// Object pool for RenderInvalidatedEventArgs to reduce allocations during animations.
    /// </summary>
    public static readonly ObjectPool<RenderInvalidatedEventArgs> RenderInvalidatedEventArgs = 
        new DefaultObjectPool<RenderInvalidatedEventArgs>(
            new RenderInvalidatedEventArgsPooledObjectPolicy(), 
            maximumRetained: 100);
}

/// <summary>
/// Pooled object policy for RenderInvalidatedEventArgs.
/// </summary>
internal sealed class RenderInvalidatedEventArgsPooledObjectPolicy : IPooledObjectPolicy<RenderInvalidatedEventArgs>
{
    public RenderInvalidatedEventArgs Create()
    {
        return new RenderInvalidatedEventArgs();
    }

    public bool Return(RenderInvalidatedEventArgs obj)
    {
        obj.Reset();
        return true;
    }
}
using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Media.Proxy;

/// <summary>
/// Creates an <see cref="IProxyGenerator"/> for a store, so an extension can register the factory before the
/// store exists.
/// </summary>
public interface IProxyGeneratorFactory
{
    IProxyGenerator Create(IProxyStore store);
}

/// <summary>
/// The <see cref="IProxyGeneratorFactory"/> instances that proxy extensions register at load and remove at unload.
/// </summary>
public static class ProxyGeneratorRegistry
{
    private static readonly ILogger s_logger = Log.CreateLogger(nameof(ProxyGeneratorRegistry));
    private static readonly List<IProxyGeneratorFactory> s_factories = [];
    private static readonly object s_lock = new();

    /// <summary>
    /// Raised outside the lock after the registered factories change, so a cached generator can be dropped.
    /// </summary>
    public static event EventHandler? Changed;

    public static void Register(IProxyGeneratorFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (s_lock)
        {
            s_factories.Add(factory);
        }

        RaiseChanged();
    }

    public static bool Unregister(IProxyGeneratorFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        bool removed;
        lock (s_lock)
        {
            removed = s_factories.Remove(factory);
        }

        if (removed)
            RaiseChanged();

        return removed;
    }

    // Isolate each subscriber: a throwing Changed handler (during extension load/unload) must not abort
    // the registry mutation or prevent the other subscribers — e.g. the queue's cache invalidation —
    // from running.
    private static void RaiseChanged()
    {
        if (Changed is not { } handler)
            return;

        foreach (Delegate subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber)(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "A ProxyGeneratorRegistry.Changed subscriber threw; continuing with the rest.");
            }
        }
    }

    /// <summary>Snapshot of registered factories in registration order. The first registered factory
    /// is the composition root's default generator.</summary>
    public static IReadOnlyList<IProxyGeneratorFactory> Enumerate()
    {
        lock (s_lock)
        {
            return [.. s_factories];
        }
    }
}

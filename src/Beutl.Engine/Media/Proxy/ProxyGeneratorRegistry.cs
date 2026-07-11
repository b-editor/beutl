using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Media.Proxy;

/// <summary>
/// Creates an <see cref="IProxyGenerator"/> bound to a specific <see cref="IProxyStore"/>. A factory
/// is registered into <see cref="ProxyGeneratorRegistry"/> by a <see cref="Beutl.Extensibility.ProxyExtension"/>
/// at extension <c>Load()</c> time; the composition root (<c>ProxyMediaServices</c>) invokes
/// <see cref="Create"/> once the store exists, so a generator never has to capture the store at
/// registration time. This mirrors <c>IDecoderInfo</c>/<c>DecoderRegistry</c>: a store-agnostic
/// descriptor that is opened/bound lazily by the caller.
/// </summary>
public interface IProxyGeneratorFactory
{
    IProxyGenerator Create(IProxyStore store);
}

/// <summary>
/// Static registry of <see cref="IProxyGeneratorFactory"/> instances, mirroring
/// <see cref="Media.Decoding.DecoderRegistry"/>. A <see cref="Beutl.Extensibility.ProxyExtension"/>
/// registers its factory here at <c>Load()</c> and unregisters at <c>Unload()</c>; the composition
/// root enumerates the registry to pick the generator for the proxy job queue.
/// </summary>
public static class ProxyGeneratorRegistry
{
    private static readonly ILogger s_logger = Log.CreateLogger(nameof(ProxyGeneratorRegistry));
    private static readonly List<IProxyGeneratorFactory> s_factories = [];
    private static readonly object s_lock = new();

    /// <summary>
    /// Raised after the set of registered factories changes. A composition root that caches a resolved
    /// generator (e.g. <c>ProxyJobQueue</c>) subscribes to drop its cache so it neither keeps rooting an
    /// unregistered factory's generator nor keeps invoking one whose extension unloaded. Fired outside
    /// the internal lock, so a handler may safely re-enter <see cref="Enumerate"/>.
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

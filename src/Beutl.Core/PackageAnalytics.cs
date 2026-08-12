namespace Beutl;

/// <summary>
/// Internal bridge between package lifecycle code and the executable-owned telemetry host.
/// Keeping the bridge in Core avoids exposing telemetry lifecycle types to library consumers.
/// </summary>
internal static class PackageAnalytics
{
    private static PackageAnalyticsHandlers? s_handlers;

    internal static void Configure(PackageAnalyticsHandlers handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        Volatile.Write(ref s_handlers, handlers);
    }

    internal static PackageAnalyticsHandlers? ExchangeHandlers(PackageAnalyticsHandlers? handlers)
    {
        return Interlocked.Exchange(ref s_handlers, handlers);
    }

    internal static PackageAnalyticsOperation StartExtensionLoad()
    {
        return new PackageAnalyticsOperation(Volatile.Read(ref s_handlers)?.StartExtensionLoad());
    }

    internal static void RecordExtensionManageQueued()
    {
        Volatile.Read(ref s_handlers)?.RecordExtensionManageQueued();
    }

    internal static void RegisterTrustedFeature(Type type, string featureId)
    {
        Volatile.Read(ref s_handlers)?.RegisterTrustedFeature(type, featureId);
    }

    internal static void UnregisterTrustedFeature(Type type)
    {
        Volatile.Read(ref s_handlers)?.UnregisterTrustedFeature(type);
    }

    internal static void RecordExtensionLoaded(Type type)
    {
        Volatile.Read(ref s_handlers)?.RecordExtensionLoaded(type);
    }
}

internal sealed record PackageAnalyticsHandlers(
    Func<Action<bool>?> StartExtensionLoad,
    Action RecordExtensionManageQueued,
    Action<Type, string> RegisterTrustedFeature,
    Action<Type> UnregisterTrustedFeature,
    Action<Type> RecordExtensionLoaded);

internal sealed class PackageAnalyticsOperation : IDisposable
{
    private Action<bool>? _complete;

    internal PackageAnalyticsOperation(Action<bool>? complete)
    {
        _complete = complete;
    }

    internal void Complete()
    {
        Interlocked.Exchange(ref _complete, null)?.Invoke(true);
    }

    internal void Fail()
    {
        Interlocked.Exchange(ref _complete, null)?.Invoke(false);
    }

    public void Dispose()
    {
        Fail();
    }
}

namespace Beutl.Media.Proxy;

public interface IProxyStore
{
    string StoreRootPath { get; }

    ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset);

    IReadOnlyList<ProxyEntry> Enumerate();

    void Register(ProxyEntry entry);

    bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null);

    bool Delete(ProxyFingerprint source, ProxyPreset preset);

    void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc);

    long GetTotalBytes();

    long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths);

    Task FlushAsync(CancellationToken cancellationToken);

    Task ReconcileAsync(CancellationToken cancellationToken);

    event EventHandler<ProxyStoreChangedEventArgs>? Changed;
}

public sealed class ProxyStoreChangedEventArgs : EventArgs
{
    public required ProxyFingerprint Source { get; init; }

    public required ProxyPreset Preset { get; init; }

    public required ProxyStoreChangeKind Kind { get; init; }
}

public enum ProxyStoreChangeKind
{
    Registered,
    StateChanged,
    Deleted,
    Touched,

    // The whole store was replaced (e.g. the proxy store root changed): every entry may differ, so a
    // consumer should do a full refresh rather than update a single source. Source/Preset are not
    // meaningful for this kind.
    Reset,
}

using Beutl.Media.Proxy;

namespace Beutl.UnitTests.TestInfrastructure;

// Forwards every member to the inner store; fakes override only what they change.
public class DelegatingProxyStore(IProxyStore inner) : IProxyStore
{
    public virtual string StoreRootPath => inner.StoreRootPath;

    public virtual ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset) => inner.TryGet(source, preset);

    public virtual IReadOnlyList<ProxyEntry> Enumerate() => inner.Enumerate();

    public virtual void Register(ProxyEntry entry) => inner.Register(entry);

    public virtual bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null)
        => inner.TryTransition(source, preset, newState, failureReason);

    public virtual bool Delete(ProxyFingerprint source, ProxyPreset preset) => inner.Delete(source, preset);

    public virtual void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc) => inner.Touch(source, preset, nowUtc);

    public virtual long GetTotalBytes() => inner.GetTotalBytes();

    public virtual long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths) => inner.GetTotalBytes(sourceAbsolutePaths);

    public virtual Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public virtual Task ReconcileAsync(CancellationToken cancellationToken) => inner.ReconcileAsync(cancellationToken);

    public virtual event EventHandler<ProxyStoreChangedEventArgs>? Changed
    {
        add => inner.Changed += value;
        remove => inner.Changed -= value;
    }
}

// Delegates to a real store but keeps the proxy file on disk after Delete, simulating a sharing
// violation where the index entry is removed yet File.Delete could not remove the file.
public sealed class RestoreFileOnDeleteStore(IProxyStore inner) : DelegatingProxyStore(inner)
{
    public override bool Delete(ProxyFingerprint source, ProxyPreset preset)
    {
        ProxyEntry? entry = TryGet(source, preset);
        string? path = entry is null
            ? null
            : Path.Combine(StoreRootPath, entry.ProxyFileRelative.Replace('/', Path.DirectorySeparatorChar));
        byte[]? bytes = path is not null && File.Exists(path) ? File.ReadAllBytes(path) : null;

        bool result = base.Delete(source, preset);

        if (bytes is not null && path is not null)
            File.WriteAllBytes(path, bytes);
        return result;
    }
}

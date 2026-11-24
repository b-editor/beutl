using Beutl.Media.Decoding;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.TestInfrastructure;

// Forwards every member to the inner store; fakes override only what they change.
public class DelegatingProxyStore(IProxyStore inner) : IProxyStore
{
    public virtual string StoreRootPath => inner.StoreRootPath;

    public virtual ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset, VideoDeinterlaceOptions deinterlace = default) => inner.TryGet(source, preset, deinterlace);

    public virtual IReadOnlyList<ProxyEntry> Enumerate() => inner.Enumerate();

    public virtual void Register(ProxyEntry entry) => inner.Register(entry);

    public virtual bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null, VideoDeinterlaceOptions deinterlace = default)
        => inner.TryTransition(source, preset, newState, failureReason, deinterlace);

    public virtual bool Delete(ProxyFingerprint source, ProxyPreset preset, VideoDeinterlaceOptions deinterlace = default) => inner.Delete(source, preset, deinterlace);

    public virtual void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc, VideoDeinterlaceOptions deinterlace = default) => inner.Touch(source, preset, nowUtc, deinterlace);

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

// Holds nothing and changes nothing; fakes override only the members their tests exercise.
public class StubProxyStore(string storeRootPath) : IProxyStore
{
    public virtual string StoreRootPath => storeRootPath;

    public virtual ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset, VideoDeinterlaceOptions deinterlace = default) => null;

    public virtual IReadOnlyList<ProxyEntry> Enumerate() => [];

    public virtual void Register(ProxyEntry entry)
    {
    }

    public virtual bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null, VideoDeinterlaceOptions deinterlace = default)
        => false;

    public virtual bool Delete(ProxyFingerprint source, ProxyPreset preset, VideoDeinterlaceOptions deinterlace = default) => false;

    public virtual void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc, VideoDeinterlaceOptions deinterlace = default)
    {
    }

    public virtual long GetTotalBytes() => 0;

    public virtual long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths) => 0;

    public virtual Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual Task ReconcileAsync(CancellationToken cancellationToken) => Task.CompletedTask;

#pragma warning disable CS0067 // A stub never reports a change.
    public event EventHandler<ProxyStoreChangedEventArgs>? Changed;
#pragma warning restore CS0067
}

// Counts registrations and fails the first failuresBeforeSuccess of them; holds the last one registered.
public sealed class CountingStore(string root, int failuresBeforeSuccess) : StubProxyStore(root)
{
    public int RegisterAttempts { get; private set; }

    public ProxyEntry? LastRegistered { get; private set; }

    public Action<int>? RegisterAttempted { get; set; }

    public bool MutateBeforeFailure { get; init; }

    public bool FailRollback { get; init; }

    public void Seed(ProxyEntry entry) => LastRegistered = entry;

    public override ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset, VideoDeinterlaceOptions deinterlace = default)
        => LastRegistered is { } entry && entry.Source == source && entry.Preset == preset && entry.Deinterlace == deinterlace.Normalize() ? entry : null;

    public override void Register(ProxyEntry entry)
    {
        RegisterAttempts++;
        if (FailRollback && RegisterAttempts > 1)
            throw new IOException("rollback registration failed");
        if (MutateBeforeFailure)
            LastRegistered = entry;
        RegisterAttempted?.Invoke(RegisterAttempts);
        if (RegisterAttempts <= failuresBeforeSuccess)
            throw new InvalidOperationException("index locked");

        LastRegistered = entry;
    }

    public override bool Delete(ProxyFingerprint source, ProxyPreset preset, VideoDeinterlaceOptions deinterlace = default)
    {
        if (TryGet(source, preset, deinterlace) is null)
            return false;
        if (FailRollback)
            throw new IOException("rollback deletion failed");
        LastRegistered = null;
        return true;
    }
}

// Delegates to a real store but keeps the proxy file on disk after Delete, simulating a sharing
// violation where the index entry is removed yet File.Delete could not remove the file.
public sealed class RestoreFileOnDeleteStore(IProxyStore inner) : DelegatingProxyStore(inner)
{
    public override bool Delete(ProxyFingerprint source, ProxyPreset preset, VideoDeinterlaceOptions deinterlace = default)
    {
        ProxyEntry? entry = TryGet(source, preset);
        string? path = entry is null
            ? null
            : Path.Combine(StoreRootPath, entry.ProxyFileRelative.Replace('/', Path.DirectorySeparatorChar));
        byte[]? bytes = path is not null && File.Exists(path) ? File.ReadAllBytes(path) : null;

        bool result = base.Delete(source, preset, deinterlace);

        if (bytes is not null && path is not null)
            File.WriteAllBytes(path, bytes);
        return result;
    }
}

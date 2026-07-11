namespace Beutl.Media.Proxy;

/// <summary>
/// Read-only view of the proxy store's size cap for UI display. The eviction <em>strategy</em>
/// (LRU ordering, open-project affinity, disk-pressure sweeps, active-generation protection) is a
/// closed MVP decision baked into <see cref="ProxyEvictionService"/> and is intentionally not a
/// pluggable seam here — same rationale as the closed <see cref="ProxyPreset"/> set (FR-017).
/// </summary>
public interface IProxyStoreCapInfo
{
    long MaxTotalBytes { get; }
}

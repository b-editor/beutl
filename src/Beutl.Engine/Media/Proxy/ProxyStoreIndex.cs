namespace Beutl.Media.Proxy;

public sealed record ProxyStoreIndex
{
    public int Version { get; init; } = 1;

    public List<ProxyEntry> Entries { get; init; } = [];

    public DateTime? LastEvictionUtc { get; init; }
}

public sealed record ProxySourceMetadata
{
    public int Version { get; init; } = 1;

    public ProxyFingerprint Source { get; init; }

    public List<ProxyEntry> Entries { get; init; } = [];
}

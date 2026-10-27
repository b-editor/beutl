namespace Beutl.Media.Proxy;

public sealed record ProxyStoreIndex
{
    // 3: SDR sources decode by their transfer tags, so proxies made from the forced sRGB reading are stale.
    public const int CurrentVersion = 3;

    public int Version { get; init; } = CurrentVersion;

    public List<ProxyEntry> Entries { get; init; } = [];

    public DateTime? LastEvictionUtc { get; init; }
}

public sealed record ProxySourceMetadata
{
    // 3: as ProxyStoreIndex, so a rebuilt index cannot pick up the stale proxies either.
    public const int CurrentVersion = 3;

    public int Version { get; init; } = CurrentVersion;

    public ProxyFingerprint Source { get; init; }

    public List<ProxyEntry> Entries { get; init; } = [];
}

namespace Beutl.Media.Proxy;

public sealed record ProxyStoreIndex
{
    // 3: SDR sources decode by their transfer tags, so proxies made from the forced sRGB reading are stale.
    // 4: BT.709-family video decodes and encodes with QuickTime's curve, so proxies written with the BT.709 OETF would
    //    read back with different shadows.
    public const int CurrentVersion = 4;

    public int Version { get; init; } = CurrentVersion;

    public List<ProxyEntry> Entries { get; init; } = [];

    public DateTime? LastEvictionUtc { get; init; }
}

public sealed record ProxySourceMetadata
{
    // 3, 4: as ProxyStoreIndex, so a rebuilt index cannot pick up the stale proxies either.
    public const int CurrentVersion = 4;

    public int Version { get; init; } = CurrentVersion;

    public ProxyFingerprint Source { get; init; }

    public List<ProxyEntry> Entries { get; init; } = [];
}

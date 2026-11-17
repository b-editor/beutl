namespace Beutl.Media.Proxy;

public sealed record ProxyStoreIndex
{
    // 3: SDR sources decode by their transfer tags, so proxies made from the forced sRGB reading are stale.
    // 4: BT.709-family video decodes and encodes with QuickTime's curve, so proxies written with the BT.709 OETF would
    //    read back with different shadows.
    // 5: Media Foundation read non-square-pixel frames at their display size, so older proxies can contain distorted rows.
    public const int CurrentVersion = 5;

    public int Version { get; init; } = CurrentVersion;

    public List<ProxyEntry> Entries { get; init; } = [];

    public DateTime? LastEvictionUtc { get; init; }
}

public sealed record ProxySourceMetadata
{
    // Match the index version when decoded pixels change so recovery skips the same stale proxies.
    public const int CurrentVersion = 5;

    public int Version { get; init; } = CurrentVersion;

    public ProxyFingerprint Source { get; init; }

    public List<ProxyEntry> Entries { get; init; } = [];
}

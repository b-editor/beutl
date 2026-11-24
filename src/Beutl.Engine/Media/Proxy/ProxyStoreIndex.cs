namespace Beutl.Media.Proxy;

public sealed record ProxyStoreIndex
{
    // 3: SDR sources decode by their transfer tags, so proxies made from the forced sRGB reading are stale.
    // 4: BT.709-family video decodes and encodes with QuickTime's curve, so proxies written with the BT.709 OETF would
    //    read back with different shadows.
    // 5: Media Foundation read non-square-pixel frames at their display size, so older proxies can contain distorted rows.
    // 6: Transfer interpretation uses the original color tags. Proxy entries also carry the deinterlace
    //    settings they were generated with, the original scan order and duration, and the effective processing status.
    public const int CurrentVersion = 6;

    public int Version { get; init; } = CurrentVersion;

    public List<ProxyEntry> Entries { get; init; } = [];

    public DateTime? LastEvictionUtc { get; init; }
}

public sealed record ProxySourceMetadata
{
    // Recovery requires the same original stream metadata as the index.
    public const int CurrentVersion = ProxyStoreIndex.CurrentVersion;

    public int Version { get; init; } = CurrentVersion;

    public ProxyFingerprint Source { get; init; }

    public List<ProxyEntry> Entries { get; init; } = [];
}

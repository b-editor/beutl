namespace Beutl.Media.Proxy;

public sealed record ProxyStoreIndex
{
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;

    public List<ProxyEntry> Entries { get; init; } = [];

    public DateTime? LastEvictionUtc { get; init; }
}

public sealed record ProxySourceMetadata
{
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;

    public ProxyFingerprint Source { get; init; }

    public List<ProxyEntry> Entries { get; init; } = [];
}

namespace Beutl.Media.Source.Proxy;

public enum ProxyStatus
{
    NotGenerated,
    Generating,
    Available,
    Stale,
    Failed,
    SkippedTooSmall,
}

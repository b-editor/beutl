namespace Beutl.Media.Proxy;

/// <summary>
/// The proxy store's size cap, for UI display.
/// </summary>
public interface IProxyStoreCapInfo
{
    long MaxTotalBytes { get; }
}

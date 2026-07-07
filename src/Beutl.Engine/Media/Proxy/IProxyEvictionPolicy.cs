namespace Beutl.Media.Proxy;

public interface IProxyEvictionPolicy
{
    long MaxTotalBytes { get; }
}

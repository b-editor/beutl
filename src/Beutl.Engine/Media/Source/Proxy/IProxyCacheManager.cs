using System.Diagnostics.CodeAnalysis;

namespace Beutl.Media.Source.Proxy;

public interface IProxyCacheManager
{
    bool TryGetProxyPath(string originalPath, [NotNullWhen(true)] out string? proxyPath);

    ProxyStatus GetStatus(string originalPath);

    string ComputeKey(string originalPath);

    ProxyEntry? TryGetEntry(string originalPath);

    IEnumerable<ProxyEntry> Enumerate();

    long GetTotalSizeBytes();

    void Invalidate(string originalPath);

    void Delete(string originalPath);

    void TrimCache(long maxBytes);

    void Register(ProxyEntry entry);

    void MarkGenerating(string originalPath);

    void MarkFailed(string originalPath);
}

namespace Beutl.Media.Proxy;

public static class ProxyStateTransitions
{
    public static bool IsLegal(ProxyState from, ProxyState to)
        => (from, to) switch
        {
            (ProxyState.None, ProxyState.Generating) => true,
            (ProxyState.Generating, ProxyState.Ready) => true,
            (ProxyState.Generating, ProxyState.Failed) => true,
            (ProxyState.Generating, ProxyState.None) => true,
            (ProxyState.Generating, ProxyState.Partial) => true,
            (ProxyState.Ready, ProxyState.Stale) => true,
            (ProxyState.Ready, ProxyState.None) => true,
            (ProxyState.Stale, ProxyState.Generating) => true,
            (ProxyState.Stale, ProxyState.None) => true,
            (ProxyState.Failed, ProxyState.Generating) => true,
            (ProxyState.Failed, ProxyState.None) => true,
            (ProxyState.Partial, ProxyState.None) => true,
            _ => false
        };
}

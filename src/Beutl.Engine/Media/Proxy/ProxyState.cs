namespace Beutl.Media.Proxy;

public enum ProxyState
{
    None = 0,
    Generating = 1,
    Ready = 2,
    Stale = 3,
    Failed = 4,
    Partial = 5,
}

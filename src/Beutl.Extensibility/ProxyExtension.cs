using Beutl.Media.Proxy;

namespace Beutl.Extensibility;

// Mirrors DecodingExtension: register a proxy-generator factory into ProxyGeneratorRegistry at Load.
public abstract class ProxyExtension : Extension
{
    private IProxyGeneratorFactory? _factory;

    public abstract IProxyGeneratorFactory GetProxyGeneratorFactory();

    public override void Load(IExtensionContext context)
    {
        _factory = GetProxyGeneratorFactory();
        ProxyGeneratorRegistry.Register(_factory);
    }

    public override void Unload(IExtensionContext context)
    {
        if (_factory != null)
        {
            ProxyGeneratorRegistry.Unregister(_factory);
            _factory = null;
        }
    }
}

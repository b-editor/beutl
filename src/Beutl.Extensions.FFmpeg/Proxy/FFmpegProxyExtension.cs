using Beutl.Extensibility;
using Beutl.Media.Proxy;

namespace Beutl.Extensions.FFmpeg.Proxy;

// Built-in proxy generator backed by the FFmpeg worker. Registered into ProxyGeneratorRegistry at
// Load so ProxyMediaServices can pick it up without a compile-time reference from the app to this
// package; mirrors how FFmpegDecodingExtension registers IDecoderInfo into DecoderRegistry.
public sealed class FFmpegProxyExtension : ProxyExtension
{
    public override IProxyGeneratorFactory GetProxyGeneratorFactory()
        => new FFmpegProxyGeneratorFactory();

    private sealed class FFmpegProxyGeneratorFactory : IProxyGeneratorFactory
    {
        public IProxyGenerator Create(IProxyStore store)
            => new FFmpegProxyGenerator(store);
    }
}

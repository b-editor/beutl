using Beutl.Extensibility;
using Beutl.Media.Proxy;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Extensions.FFmpeg.Proxy;

// Built-in proxy generator backed by the FFmpeg worker. Registered into ProxyGeneratorRegistry at
// Load so ProxyMediaServices can pick it up without a compile-time reference from the app to this
// package; mirrors how FFmpegDecodingExtension registers IDecoderInfo into DecoderRegistry.
// [Export] is required for an external FFmpeg package: PackageManager.LoadExtension only
// instantiates exported types, so without it the package would register decoders but no proxy generator.
[Export]
public sealed class FFmpegProxyExtension : ProxyExtension
{
    private INotificationService? _notifications;

    public override void Load(IExtensionContext context)
    {
        _notifications = context.Services.GetRequiredService<INotificationService>();
        base.Load(context);
    }

    public override void Unload(IExtensionContext context)
    {
        base.Unload(context);
        _notifications = null;
    }

    public override IProxyGeneratorFactory GetProxyGeneratorFactory()
        => new FFmpegProxyGeneratorFactory(_notifications ?? throw new InvalidOperationException("The extension is not loaded."));

    private sealed class FFmpegProxyGeneratorFactory(INotificationService notifications) : IProxyGeneratorFactory
    {
        public IProxyGenerator Create(IProxyStore store)
            => new FFmpegProxyGenerator(store, notifications);
    }
}

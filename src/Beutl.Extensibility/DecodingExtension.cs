using System.Runtime.CompilerServices;
using Beutl.Media.Decoding;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Extensibility;

public abstract class DecodingExtension : Extension
{
    private readonly ConditionalWeakTable<IExtensionContext, IDecoderInfo> _decoders = new();

    public abstract IDecoderInfo GetDecoderInfo();

    public override void Load(IExtensionContext context)
    {
        var registry = context.Services.GetRequiredService<DecoderRegistry>();
        IDecoderInfo decoder = GetDecoderInfo();
        _decoders.AddOrUpdate(context, decoder);
        registry.Register(decoder);
    }

    public override void Unload(IExtensionContext context)
    {
        if (_decoders.Remove(context, out IDecoderInfo? decoder))
        {
            context.Services.GetRequiredService<DecoderRegistry>().Unregister(decoder);
        }
    }
}

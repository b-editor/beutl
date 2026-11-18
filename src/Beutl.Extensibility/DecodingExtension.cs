using Beutl.Media.Decoding;

namespace Beutl.Extensibility;

public abstract class DecodingExtension : Extension
{
    private IDecoderInfo? _decoderInfo;

    public abstract IDecoderInfo GetDecoderInfo();

    public override void Load(IExtensionContext context)
    {
        _decoderInfo = GetDecoderInfo();
        DecoderRegistry.Register(_decoderInfo);
    }

    public override void Unload(IExtensionContext context)
    {
        if (_decoderInfo != null)
        {
            DecoderRegistry.Unregister(_decoderInfo);
            _decoderInfo = null;
        }
    }
}

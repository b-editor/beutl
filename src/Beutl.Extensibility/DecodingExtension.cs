using Beutl.Media.Decoding;

namespace Beutl.Extensibility;

public abstract class DecodingExtension : Extension
{
    private IDecoderInfo? _decoderInfo;

    public abstract IDecoderInfo GetDecoderInfo();

    public override void Load()
    {
        _decoderInfo = GetDecoderInfo();
        DecoderRegistry.Register(_decoderInfo);
    }

    public override void Unload()
    {
        if (_decoderInfo != null)
        {
            DecoderRegistry.Unregister(_decoderInfo);
            _decoderInfo = null;
        }
    }
}

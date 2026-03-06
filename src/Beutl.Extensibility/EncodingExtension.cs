using Beutl.Media.Encoding;

namespace Beutl.Extensibility;

[Obsolete("Use EncodingController instead.")]
public abstract class EncodingExtension : Extension
{
    private IEncoderInfo? _encoderInfo;

    public abstract IEncoderInfo GetEncoderInfo();

    public override void Load()
    {
        _encoderInfo = GetEncoderInfo();
        EncoderRegistry.Register(_encoderInfo);
    }

    public override void Unload()
    {
        if (_encoderInfo != null)
        {
            EncoderRegistry.Unregister(_encoderInfo);
            _encoderInfo = null;
        }
    }
}

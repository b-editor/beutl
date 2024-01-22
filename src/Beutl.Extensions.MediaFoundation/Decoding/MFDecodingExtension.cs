using Beutl.Extensibility;
using Beutl.Media.Decoding;

using SharpDX.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

[Export]
public sealed class MFDecodingExtension : DecodingExtension
{
    public override string Name => "MediaFoundationDecoding";

    public override string DisplayName => "Media Foundation Decoding";

    public override IDecoderInfo GetDecoderInfo()
    {
        return new MFDecoderInfo();
    }

    public override void Load()
    {
        base.Load();

        MediaManager.Startup();
    }
}

using Beutl.Media.Encoding;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Encoding;
#else
namespace Beutl.Extensions.MediaFoundation.Encoding;
#endif

public class MFVideoEncoderSettings : VideoEncoderSettings
{
    public MFVideoFormat Format { get; set; }
}

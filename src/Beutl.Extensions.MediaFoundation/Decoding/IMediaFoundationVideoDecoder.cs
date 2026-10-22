#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

internal interface IMediaFoundationVideoDecoder : IDisposable
{
    MFMediaInfo GetMediaInfo();

    int ReadFrame(int frame, nint buf);
}

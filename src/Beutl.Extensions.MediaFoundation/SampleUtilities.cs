// https://github.com/amate/MFVideoReader

using System.Diagnostics;

using Vortice.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public class SampleUtilities
{
    public static unsafe int SampleCopyToBuffer(IMFSample pSample, nint buf, int copyBufferSize)
    {
        using IMFMediaBuffer spBuffer = pSample.ConvertToContiguousBuffer();
        spBuffer.Lock(out nint pData, out _, out int currentLength);

        Debug.Assert(copyBufferSize <= currentLength);
        Buffer.MemoryCopy((void*)pData, (void*)buf, copyBufferSize, copyBufferSize);

        spBuffer.Unlock();

        return copyBufferSize;
    }
}

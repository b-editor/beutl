// https://github.com/amate/MFVideoReader

using System.Diagnostics;

using SharpDX.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public class SampleUtilities
{
    public static unsafe int SampleCopyToBuffer(Sample pSample, nint buf, int copyBufferPos, int copyBufferSize)
    {
        using MediaBuffer spBuffer = pSample.ConvertToContiguousBuffer();
        nint pData = spBuffer.Lock(out _, out int currentLength);

        //ATLTRACE(L"currentLength: %d\n", currentLength);
        Debug.Assert((copyBufferPos + copyBufferSize) <= currentLength);
        Buffer.MemoryCopy((void*)(pData + copyBufferPos), (void*)buf, copyBufferSize, copyBufferSize);

        spBuffer.Unlock();

        return copyBufferSize;
    }

    public static int SampleCopyToBuffer(Sample pSample, nint buf, int copyBufferSize)
    {
        return SampleCopyToBuffer(pSample, buf, 0, copyBufferSize);
    }
}

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
    public static unsafe int SampleCopyToBuffer(IMFSample pSample, nint buf, int copyBufferPos, int copyBufferSize)
    {
        using IMFMediaBuffer spBuffer = pSample.ConvertToContiguousBuffer();
        spBuffer.Lock(out nint pData, out _, out int currentLength);

        //ATLTRACE(L"currentLength: %d\n", currentLength);
        Debug.Assert((copyBufferPos + copyBufferSize) <= currentLength);
        Buffer.MemoryCopy((void*)(pData + copyBufferPos), (void*)buf, copyBufferSize, copyBufferSize);

        spBuffer.Unlock();

        return copyBufferSize;
    }

    public static int SampleCopyToBuffer(IMFSample pSample, nint buf, int copyBufferSize)
    {
        return SampleCopyToBuffer(pSample, buf, 0, copyBufferSize);
    }
}

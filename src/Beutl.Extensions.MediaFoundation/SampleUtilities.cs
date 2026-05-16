// https://github.com/amate/MFVideoReader

using Beutl.Logging;

using Microsoft.Extensions.Logging;

using Vortice.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public class SampleUtilities
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(SampleUtilities));

    // Copies `copyBufferSize` bytes from the MF sample (starting at
    // copyBufferPos) into the destination buffer. Returns the number of
    // bytes that were actually copied — callers should treat the unwritten
    // tail as zero (Lock returns a transient pointer so we cannot expose it).
    //
    // The original implementation used a Debug.Assert and an unconditional
    // Buffer.MemoryCopy, which crashed Release builds with CLR Execution
    // Engine Exceptions when an aspect-corrected `copyBufferSize` exceeded
    // the actual sample length — for example anamorphic SD content where
    // the corrected destRect ends up wider than the native YUY2 frame
    // Source Reader emits. Clamp at runtime, zero the rest of the
    // destination so partial frames don't read whatever the ArrayPool
    // happened to hand us, and log the mismatch so the underlying issue
    // is visible instead of producing a hard crash.
    public static unsafe int SampleCopyToBuffer(IMFSample pSample, nint buf, int copyBufferPos, int copyBufferSize)
    {
        if (copyBufferSize <= 0)
        {
            return 0;
        }

        using IMFMediaBuffer spBuffer = pSample.ConvertToContiguousBuffer();
        spBuffer.Lock(out nint pData, out _, out int currentLength);
        try
        {
            int available = currentLength - copyBufferPos;
            if (available <= 0)
            {
                // Nothing to copy — start position is past the end of the sample.
                s_logger.LogWarning(
                    "Sample copy skipped: requested offset {Pos} exceeds sample length {Length}",
                    copyBufferPos, currentLength);
                System.Runtime.InteropServices.NativeMemory.Clear((void*)buf, (nuint)copyBufferSize);
                return 0;
            }

            int bytesToCopy = System.Math.Min(copyBufferSize, available);
            if (bytesToCopy < copyBufferSize)
            {
                s_logger.LogWarning(
                    "Sample copy clamped: requested {Requested} bytes from offset {Pos} but sample only has {Available} (length={Length}). Output tail will be zero-padded.",
                    copyBufferSize, copyBufferPos, available, currentLength);
            }

            Buffer.MemoryCopy((void*)(pData + copyBufferPos), (void*)buf, copyBufferSize, bytesToCopy);
            if (bytesToCopy < copyBufferSize)
            {
                // Zero-fill the destination tail so callers see deterministic
                // output instead of leftover ArrayPool / native heap content.
                System.Runtime.InteropServices.NativeMemory.Clear(
                    (void*)(buf + bytesToCopy), (nuint)(copyBufferSize - bytesToCopy));
            }
            return bytesToCopy;
        }
        finally
        {
            spBuffer.Unlock();
        }
    }

    public static int SampleCopyToBuffer(IMFSample pSample, nint buf, int copyBufferSize)
    {
        return SampleCopyToBuffer(pSample, buf, 0, copyBufferSize);
    }
}

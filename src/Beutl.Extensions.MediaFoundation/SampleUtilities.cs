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
    /// <summary>
    /// Copies the sample into <paramref name="buf"/> as tightly packed rows of <paramref name="rowBytes"/> bytes.
    /// </summary>
    /// <param name="pSample">The decoded sample.</param>
    /// <param name="buf">The destination buffer.</param>
    /// <param name="copyBufferSize">The number of bytes to write to <paramref name="buf"/>; a whole number of rows.</param>
    /// <param name="rowBytes">
    /// Bytes of one tightly packed row in <paramref name="buf"/> (width × bytes per pixel), or 0 to copy one block.
    /// The decoder's pitch can be wider than this (e.g. YUY2 at width 1080: 2176 bytes, not 2160). Copying the whole
    /// buffer in one go would then shift every row by the padding and shear the picture without any error, so when the
    /// sample exposes <see cref="IMF2DBuffer"/> the rows are copied one by one using its pitch.
    /// </param>
    public static unsafe int SampleCopyToBuffer(IMFSample pSample, nint buf, int copyBufferSize, int rowBytes)
    {
        Debug.Assert(rowBytes <= 0 || copyBufferSize % rowBytes == 0, "copyBufferSize must be a whole number of rows.");
        using IMFMediaBuffer spBuffer = pSample.ConvertToContiguousBuffer();
        if (rowBytes > 0 && copyBufferSize % rowBytes == 0)
        {
            using IMF2DBuffer? buffer2D = spBuffer.QueryInterfaceOrNull<IMF2DBuffer>();
            if (buffer2D != null)
            {
                buffer2D.Lock2D(out nint scan0, out int pitch);
                try
                {
                    int rows = copyBufferSize / rowBytes;
                    if (pitch == rowBytes)
                    {
                        Buffer.MemoryCopy((void*)scan0, (void*)buf, copyBufferSize, copyBufferSize);
                    }
                    else
                    {
                        // scan0 is the first (top) row and pitch may be negative for bottom-up images,
                        // so scan0 + y * pitch addresses row y in both cases.
                        for (int y = 0; y < rows; y++)
                        {
                            Buffer.MemoryCopy(
                                (void*)(scan0 + (nint)y * pitch),
                                (void*)(buf + (nint)y * rowBytes),
                                rowBytes,
                                rowBytes);
                        }
                    }
                }
                finally
                {
                    buffer2D.Unlock2D();
                }

                return copyBufferSize;
            }
        }

        spBuffer.Lock(out nint pData, out _, out int currentLength);

        Debug.Assert(copyBufferSize <= currentLength);
        Buffer.MemoryCopy((void*)pData, (void*)buf, copyBufferSize, copyBufferSize);

        spBuffer.Unlock();

        return copyBufferSize;
    }
}

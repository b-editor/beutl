using System.IO;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

internal static class MFFrameBufferSize
{
    public const int Yuy2BytesPerPixel = 2;

    public static int CalculateYuy2(int width, int height)
        => Calculate(width, height, Yuy2BytesPerPixel);

    public static int Calculate(int width, int height, int bytesPerPixel)
    {
        if (width <= 0)
        {
            throw CreateInvalidSizeException(width, height, bytesPerPixel);
        }

        if (height <= 0)
        {
            throw CreateInvalidSizeException(width, height, bytesPerPixel);
        }

        if (bytesPerPixel <= 0)
        {
            throw CreateInvalidSizeException(width, height, bytesPerPixel);
        }

        long size;
        try
        {
            size = checked((long)width * height * bytesPerPixel);
        }
        catch (OverflowException)
        {
            throw CreateInvalidSizeException(width, height, bytesPerPixel);
        }

        if (size > Array.MaxLength)
        {
            throw CreateInvalidSizeException(width, height, bytesPerPixel);
        }

        return (int)size;
    }

    private static InvalidDataException CreateInvalidSizeException(int width, int height, int bytesPerPixel)
        => new($"Decoded video frame buffer size is invalid: {width}x{height}x{bytesPerPixel} bytes.");
}

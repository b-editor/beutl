using System.Diagnostics;
using Beutl.Media;
using Beutl.Media.Pixel;
using MonoMac.CoreGraphics;
using MonoMac.CoreImage;
using MonoMac.CoreMedia;
using MonoMac.CoreVideo;
using MonoMac.Foundation;

namespace Beutl.Extensions.AVFoundation;

public class AVFSampleUtilities
{
    public static unsafe CVPixelBuffer? ConvertToCVPixelBuffer(Bitmap<Bgra8888> bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        var pixelBuffer = new CVPixelBuffer(width, height, CVPixelFormatType.CV32BGRA, new CVPixelBufferAttributes
        {
            PixelFormatType = CVPixelFormatType.CV32BGRA,
            Width = width,
            Height = height
        });

        var r = pixelBuffer.Lock(CVOptionFlags.None);
        if (r != CVReturn.Success) return null;

        Buffer.MemoryCopy(
            (void*)bitmap.Data, (void*)pixelBuffer.GetBaseAddress(0),
            bitmap.ByteCount, bitmap.ByteCount);

        pixelBuffer.Unlock(CVOptionFlags.None);

        return pixelBuffer;
    }

    public static unsafe Bitmap<Bgra8888>? ConvertToBgra(CMSampleBuffer buffer)
    {
        using var imageBuffer = buffer.GetImageBuffer();
        if (imageBuffer is not CVPixelBuffer pixelBuffer) return null;

        var r = pixelBuffer.Lock(CVOptionFlags.None);
        if (r != CVReturn.Success) return null;

        int width = pixelBuffer.Width;
        int height = pixelBuffer.Height;
        var bitmap = new Bitmap<Bgra8888>(width, height);
        if (pixelBuffer.ColorSpace.Model == CGColorSpaceModel.RGB && pixelBuffer.BytesPerRow == width * 4)
        {
            Buffer.MemoryCopy(
                (void*)pixelBuffer.GetBaseAddress(0), (void*)bitmap.Data,
                bitmap.ByteCount, bitmap.ByteCount);
            pixelBuffer.Unlock(CVOptionFlags.None);
            Parallel.For(0, width * height, i =>
            {
                // argb
                // bgra
                var o = bitmap.DataSpan[i];
                bitmap.DataSpan[i] = new Bgra8888(o.G, o.R, o.A, o.B);
            });
            return bitmap;
        }

        int bytesPerRow = width * height * 4;
        using (CGColorSpace colorSpace = CGColorSpace.CreateDeviceRGB())
        using (var cgContext = new CGBitmapContext(
                   bitmap.Data, width, height,
                   8, bytesPerRow, colorSpace,
                   CGBitmapFlags.ByteOrderDefault | CGBitmapFlags.PremultipliedFirst))
        using (var ciImage = CIImage.FromImageBuffer(imageBuffer))
        using (var ciContext = new CIContext(NSObjectFlag.Empty))
            // CreateCGImageで落ちる、例外なしに
        using (var cgImage = ciContext.CreateCGImage(
                   ciImage, new CGRect(0, 0, width, height), (long)CIFormat.ARGB8, colorSpace))
        {
            cgContext.DrawImage(new CGRect(0, 0, width, height), cgImage);
        }

        pixelBuffer.Unlock(CVOptionFlags.None);

        return bitmap;
    }

    public static int SampleCopyToBuffer(CMSampleBuffer buffer, nint buf, int copyBufferPos,
        int copyBufferSize)
    {
        using var dataBuffer = buffer.GetDataBuffer();
        Debug.Assert((copyBufferPos + copyBufferSize) <= dataBuffer.DataLength);
        dataBuffer.CopyDataBytes((uint)copyBufferPos, (uint)copyBufferSize, buf);

        return copyBufferSize;
    }

    public static int SampleCopyToBuffer(CMSampleBuffer buffer, nint buf, int copyBufferSize)
    {
        return SampleCopyToBuffer(buffer, buf, 0, copyBufferSize);
    }
}

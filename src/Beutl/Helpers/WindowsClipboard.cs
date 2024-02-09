using System.Runtime.InteropServices;

using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Pixel;

#if WINDOWS
using FormsDataObject = System.Windows.Forms.DataObject;
using FormsDataFormats = System.Windows.Forms.DataFormats;
using FormsClipboard = System.Windows.Forms.Clipboard;
using GdiBitmap = System.Drawing.Bitmap;
#endif

namespace Beutl.Helpers;

public static class WindowsClipboard
{
    public static void CopyImage(Bitmap<Bgra8888> image)
    {
#if WINDOWS
        var data = new FormsDataObject();
        using var pngstream = new MemoryStream();
        image.Save(pngstream, EncodedImageFormat.Png);

        using var dibstream = new MemoryStream(ConvertToDib(image));

        using var gdiBitmap = new GdiBitmap(pngstream);

        data.SetImage(gdiBitmap);
        data.SetData("PNG", false, pngstream);
        data.SetData(FormsDataFormats.Dib, dibstream);

        FormsClipboard.SetDataObject(data, true);
#endif
    }

    public static byte[] ConvertToDib(Bitmap<Bgra8888> image)
    {
        byte[] bm32bData;
        int width = image.Width;
        int height = image.Height;
        // Ensure image is 32bppARGB by painting it on a new 32bppARGB image.
        using Bitmap<Bgra8888> bm32b = image.Clone();
        bm32b.Flip(FlipMode.XY);
        bm32bData = MemoryMarshal.AsBytes(bm32b.DataSpan).ToArray();

        // BITMAPINFOHEADER struct for DIB.
        const int hdrSize = 0x28;
        byte[] fullImageArr = new byte[hdrSize + 12 + bm32bData.Length];
        Span<byte> fullImage = fullImageArr;
        //Int32 biSize;
        BitConverter.TryWriteBytes(fullImage, (uint)hdrSize);
        fullImage = fullImage.Slice(4);

        //Int32 biWidth;
        BitConverter.TryWriteBytes(fullImage, (uint)width);
        fullImage = fullImage.Slice(4);

        //Int32 biHeight;
        BitConverter.TryWriteBytes(fullImage, (uint)height);
        fullImage = fullImage.Slice(4);

        //Int16 biPlanes;
        BitConverter.TryWriteBytes(fullImage, (ushort)1);
        fullImage = fullImage.Slice(2);

        //Int16 biBitCount;
        BitConverter.TryWriteBytes(fullImage, (ushort)32);
        fullImage = fullImage.Slice(2);

        //BITMAPCOMPRESSION biCompression = BITMAPCOMPRESSION.BITFIELDS;
        BitConverter.TryWriteBytes(fullImage, (uint)3);
        fullImage = fullImage.Slice(4);

        //Int32 biSizeImage;
        BitConverter.TryWriteBytes(fullImage, (uint)bm32bData.Length);
        fullImage = fullImage.Slice(4);

        // These are all 0. Since .net clears new arrays, don't bother writing them.
        //Int32 biXPelsPerMeter = 0;
        //Int32 biYPelsPerMeter = 0;
        //Int32 biClrUsed = 0;
        //Int32 biClrImportant = 0;
        fullImage = fullImageArr;

        // The aforementioned "BITFIELDS": colour masks applied to the Int32 pixel value to get the R, G and B values.
        fullImage = fullImage.Slice(hdrSize);
        BitConverter.TryWriteBytes(fullImage, 0x00FF0000);
        fullImage = fullImage.Slice(4);
        BitConverter.TryWriteBytes(fullImage, 0x0000FF00);
        fullImage = fullImage.Slice(4);
        BitConverter.TryWriteBytes(fullImage, 0x000000FF);

        Array.Copy(bm32bData, 0, fullImageArr, hdrSize + 12, bm32bData.Length);
        return fullImageArr;
    }

    public static void WriteIntToByteArray(byte[] data, int startIndex, int bytes, bool littleEndian, uint value)
    {
        int lastByte = bytes - 1;
        if (data.Length < startIndex + bytes)
            throw new ArgumentOutOfRangeException("startIndex", "Data array is too small to write a " + bytes + "-byte value at offset " + startIndex + ".");
        for (int index = 0; index < bytes; index++)
        {
            int offs = startIndex + (littleEndian ? index : lastByte - index);
            data[offs] = (byte)(value >> (8 * index) & 0xFF);
        }
    }
}

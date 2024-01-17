using System.Runtime.InteropServices;

using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.Helpers;

public static class WindowsClipboard
{
    private const string CopyImagePowerShellCode = """
        Add-Type -AssemblyName System.Drawing
        Add-Type -AssemblyName System.Windows.Forms

        $data = New-Object Windows.Forms.DataObject
        $pngstream = New-Object System.IO.MemoryStream
        $dibstream = [System.IO.File]::OpenRead("{0}")
        $image = New-Object System.Drawing.Bitmap("{1}")
        $image.Save($pngstream, [System.Drawing.Imaging.ImageFormat]::Png)

        $data.SetImage($image)
        $data.SetData("PNG", $False, $pngstream)
        $data.SetData([Windows.Forms.DataFormats]::Dib, $dibstream)

        [Windows.Forms.Clipboard]::SetDataObject($data, $True)

        $image.Dispose()
        $pngstream.Dispose()
        $dibstream.Dispose()
        """;

    public static async Task CopyImage(Bitmap<Bgra8888> image)
    {
        // pngファイルを作成
        string pngFile = Path.GetTempFileName();
        pngFile = Path.ChangeExtension(pngFile, "png");
        string dibFile = Path.GetTempFileName();
        string ps1File = Path.ChangeExtension(Path.GetTempFileName(), "ps1");

        try
        {
            image.Save(pngFile, EncodedImageFormat.Png);

            // dibファイルを作成
            await File.WriteAllBytesAsync(dibFile, ConvertToDib(image));

            // ps1ファイルを作成
            File.WriteAllText(ps1File, string.Format(CopyImagePowerShellCode, dibFile, pngFile));


            var startInfo = new ProcessStartInfo()
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy ByPass -File \"{ps1File}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process proc = Process.Start(startInfo) ?? throw new Exception("Failed to launch 'powershell.exe'.");
            await proc.WaitForExitAsync();
        }
        finally
        {
            TryDeleteFile(pngFile);
            TryDeleteFile(dibFile);
            TryDeleteFile(ps1File);
        }
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch
        {
        }
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

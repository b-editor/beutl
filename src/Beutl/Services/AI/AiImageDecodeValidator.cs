using Beutl.Media;
using SkiaSharp;

namespace Beutl.Services.AI;

internal static class AiImageDecodeValidator
{
    internal const int MaxDimension = 8_192;
    internal const long MaxPixels = 16_777_216;
    internal const long MaxDecodedBytes = 64L * 1024 * 1024;

    public static void ValidateEncoded(Stream encoded, long maxEncodedBytes)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (maxEncodedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxEncodedBytes));
        if (!encoded.CanSeek)
            throw new InvalidDataException("AI image data must be seekable before decoding.");

        long origin = encoded.Position;
        try
        {
            if (encoded.Length - origin > maxEncodedBytes)
                throw new InvalidDataException("The AI image is too large.");
            using SKData data = SKData.Create(encoded)
                ?? throw new InvalidDataException("Failed to inspect the AI image.");
            using SKCodec codec = SKCodec.Create(data)
                ?? throw new InvalidDataException("Failed to inspect the AI image.");
            Validate(codec.Info);
        }
        finally
        {
            encoded.Position = origin;
        }
    }

    private static FileStream OpenValidatedFile(string path, long maxEncodedBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileStream stream = File.OpenRead(path);
        try
        {
            ValidateEncoded(stream, maxEncodedBytes);
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static void Validate(SKImageInfo info)
        => ValidateDimensions(info.Width, info.Height);

    public static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0
            || width > MaxDimension || height > MaxDimension
            || (long)width * height > MaxPixels
            || (long)width * height * 4 > MaxDecodedBytes)
        {
            throw new InvalidDataException("The AI image dimensions are unsupported.");
        }
    }

    public static Bitmap LoadValidatedBitmap(string path, long maxEncodedBytes)
    {
        using FileStream stream = OpenValidatedFile(path, maxEncodedBytes);
        return Bitmap.FromStream(stream);
    }
}

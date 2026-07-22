using System.Buffers.Binary;
using System.Security.Cryptography;

using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// Reads and writes immutable, row-packed, linear-premultiplied RGBA16F golden images.
/// </summary>
/// <remarks>
/// The artifact is the raw payload required by the evidence contract: row-major RGBA half-float bit patterns
/// encoded little-endian, without a header. Dimensions live in the provenance manifest and are required when
/// reading. Row padding and host byte order never enter the artifact.
/// </remarks>
internal static class Rgba16fGoldenStore
{
    private const int BytesPerChannel = sizeof(ushort);
    private const int ChannelCount = 4;
    private const int BytesPerPixel = BytesPerChannel * ChannelCount;

    public const string Extension = ".rgba16f";

    /// <summary>
    /// Writes a new golden artifact. An existing artifact is never replaced.
    /// </summary>
    public static void Write(string path, Bitmap bitmap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bitmap);
        EnsureCanonicalBitmap(bitmap, nameof(bitmap));

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 64 * 1024,
                       FileOptions.SequentialScan))
            {
                WritePixels(stream, bitmap);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: false);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Reads a raw golden artifact into an owned linear-premultiplied RGBA16F bitmap.
    /// </summary>
    public static Bitmap Read(string path, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        long payloadLength;
        try
        {
            payloadLength = checked((long)width * height * BytesPerPixel);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException($"RGBA16F golden dimensions overflow in {path}.", ex);
        }

        if (stream.Length != payloadLength)
        {
            throw new InvalidDataException(
                $"RGBA16F golden length mismatch in {path}: expected {payloadLength} bytes for {width}x{height}, "
                + $"found {stream.Length}.");
        }

        var bitmap = new Bitmap(
            width,
            height,
            BitmapColorType.RgbaF16,
            BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);

        try
        {
            ReadPixels(stream, bitmap, path);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Computes the lowercase SHA-256 digest of the complete canonical artifact.</summary>
    public static string ComputeSha256(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void WritePixels(Stream stream, Bitmap bitmap)
    {
        byte[] encodedRow = GC.AllocateUninitializedArray<byte>(checked(bitmap.Width * BytesPerPixel));
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> source = bitmap.GetRow<ushort>(y);
            for (int i = 0; i < source.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(encodedRow.AsSpan(i * BytesPerChannel), source[i]);
            }

            stream.Write(encodedRow);
        }
    }

    private static void ReadPixels(Stream stream, Bitmap bitmap, string path)
    {
        byte[] encodedRow = GC.AllocateUninitializedArray<byte>(checked(bitmap.Width * BytesPerPixel));
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadExactly(stream, encodedRow, path);
            Span<ushort> destination = bitmap.GetRow<ushort>(y);
            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = BinaryPrimitives.ReadUInt16LittleEndian(encodedRow.AsSpan(i * BytesPerChannel));
            }
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> destination, string path)
    {
        int read = 0;
        while (read < destination.Length)
        {
            int count = stream.Read(destination[read..]);
            if (count == 0)
            {
                throw new InvalidDataException(
                    $"Truncated RGBA16F golden artifact {path}: read {read} of {destination.Length} requested bytes.");
            }

            read += count;
        }
    }

    private static void EnsureCanonicalBitmap(Bitmap bitmap, string parameterName)
    {
        if (bitmap.ColorType != BitmapColorType.RgbaF16
            || bitmap.AlphaType != BitmapAlphaType.Premul
            || bitmap.ColorSpace != BitmapColorSpace.LinearSrgb)
        {
            throw new ArgumentException(
                "Golden images must use linear-sRGB, premultiplied RgbaF16 pixels.",
                parameterName);
        }
    }
}

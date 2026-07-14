using System.IO.Compression;
using System.Runtime.CompilerServices;

using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// Freezes and re-verifies golden reference renders for the 004 GPU-pass-fusion parity gates.
/// </summary>
/// <remarks>
/// <para>
/// The parity gate (contracts/observability.md O4) is SSIM ≥ 0.99 / MAE ≤ 0.02 measured over
/// <b>linear RGBA16F</b> samples. <see cref="Bitmap.Save"/> tone-maps linear F16 down to 8-bit sRGB
/// before encoding, which throws away the very precision the gate depends on, so it cannot be used to
/// store references. Instead this store serializes the <b>raw RGBA16F sample bytes</b> (the exact
/// <see cref="Bitmap.GetPixelSpan()"/> contents produced by the render target snapshot) losslessly,
/// Deflate-compressed. Compression matters: the test content sits on a black field and compresses by
/// one to two orders of magnitude, keeping each reference small enough to live in the repo.
/// </para>
/// <para>
/// Only the raw F16 sample values are load-bearing for the comparison — <see cref="ImageMetrics"/>
/// reads <c>ushort</c> samples directly and ignores color-space metadata — so a reloaded reference is
/// reconstructed as a linear-gamma RGBA16F bitmap and compared byte-region against a freshly rendered
/// bitmap of the same dimensions.
/// </para>
/// </remarks>
internal static class GoldenReferenceStore
{
    private static readonly byte[] s_magic = "BTLREF"u8.ToArray();
    private const byte FormatVersion = 1;

    /// <summary>The extension marking a frozen linear-RGBA16F reference blob.</summary>
    public const string Extension = ".rgbaf16.deflate";

    /// <summary>Absolute path of the <c>References/</c> root, resolved from this file's compile-time location.</summary>
    public static string ReferenceRoot([CallerFilePath] string thisFile = "")
        => Path.Combine(Path.GetDirectoryName(thisFile)!, "References");

    /// <summary>Absolute path of a reference blob under <paramref name="category"/> (e.g. <c>"004-baseline"</c>).</summary>
    public static string ResolvePath(string category, string name)
        => Path.Combine(ReferenceRoot(), category, name + Extension);

    /// <summary>
    /// If the reference is missing, writes <paramref name="actual"/> as the frozen reference and returns
    /// <see langword="true"/> (freshly written). Otherwise loads the frozen reference and asserts
    /// <paramref name="actual"/> matches it within the golden thresholds, returning <see langword="false"/>.
    /// </summary>
    public static bool FreezeOrAssert(string category, string name, Bitmap actual)
    {
        string path = ResolvePath(category, name);
        if (!File.Exists(path))
        {
            // Freeze-on-missing is a local convenience only. On CI (GitHub Actions sets CI) a missing reference must
            // FAIL, not self-heal to green — otherwise a deleted or renamed reference silently disables its gate
            // forever. Freeze it locally on a Vulkan-capable machine and commit the blob.
            Assert.That(
                Environment.GetEnvironmentVariable("CI"), Is.Null.Or.Empty,
                $"[golden-ref] {category}/{name}: reference is missing on CI — a frozen reference must be committed, "
                + "not self-healed. Freeze it locally (a Vulkan-capable machine) and commit the blob.");

            Save(path, actual);
            TestContext.WriteLine($"[golden-ref] wrote missing reference: {category}/{name} ({actual.Width}x{actual.Height})");
            return true;
        }

        AssertMatches(category, path, name, actual);
        return false;
    }

    /// <summary>
    /// Requires an already committed reference and asserts that <paramref name="actual"/> matches it.
    /// Unlike <see cref="FreezeOrAssert"/>, this method never creates a missing reference.
    /// </summary>
    public static void AssertExisting(string category, string name, Bitmap actual)
    {
        string path = ResolvePath(category, name);
        Assert.That(
            File.Exists(path), Is.True,
            $"[golden-ref] {category}/{name}: immutable reference is missing; restore it from source control. "
            + "Do not regenerate it from the implementation under test.");
        AssertMatches(category, path, name, actual);
    }

    private static void AssertMatches(string category, string path, string name, Bitmap actual)
    {
        using Bitmap reference = Load(path);
        Assert.That(actual.Width, Is.EqualTo(reference.Width), $"{name}: width differs from frozen reference");
        Assert.That(actual.Height, Is.EqualTo(reference.Height), $"{name}: height differs from frozen reference");

        double ssim = ImageMetrics.Ssim(reference, actual);
        double mae = ImageMetrics.MeanAbsoluteError(reference, actual);
        TestContext.WriteLine($"[golden-ref] {category}/{name} SSIM={ssim:F4} MAE={mae:F4}");
        Assert.Multiple(() =>
        {
            Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin), $"{name}: SSIM below parity floor");
            Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), $"{name}: MAE above parity ceiling");
        });
    }

    /// <summary>Serializes <paramref name="bitmap"/>'s raw RGBA16F samples to <paramref name="path"/> (Deflate-compressed).</summary>
    public static void Save(string path, Bitmap bitmap)
    {
        if (bitmap.ColorType != BitmapColorType.RgbaF16)
            throw new ArgumentException("Golden references must be RgbaF16 (linear).", nameof(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(file);
        writer.Write(s_magic);
        writer.Write(FormatVersion);
        writer.Write(bitmap.Width);
        writer.Write(bitmap.Height);

        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        writer.Write(pixels.Length);
        writer.Flush();

        using var deflate = new DeflateStream(file, CompressionLevel.SmallestSize, leaveOpen: true);
        deflate.Write(pixels);
    }

    /// <summary>Reconstructs a linear-RGBA16F <see cref="Bitmap"/> from a frozen reference blob.</summary>
    public static Bitmap Load(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(file);

        Span<byte> magic = stackalloc byte[s_magic.Length];
        reader.Read(magic);
        if (!magic.SequenceEqual(s_magic))
            throw new InvalidDataException($"Not a golden reference blob: {path}");
        byte version = reader.ReadByte();
        if (version != FormatVersion)
            throw new InvalidDataException($"Unsupported golden reference version {version} in {path}");

        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        int byteCount = reader.ReadInt32();

        var bitmap = new Bitmap(width, height, BitmapColorType.RgbaF16, BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);
        Span<byte> pixels = bitmap.GetPixelSpan();
        if (pixels.Length != byteCount)
        {
            bitmap.Dispose();
            throw new InvalidDataException(
                $"Reference byte count mismatch in {path}: stored {byteCount}, reconstructed {pixels.Length}.");
        }

        using var deflate = new DeflateStream(file, CompressionMode.Decompress);
        int read = 0;
        while (read < pixels.Length)
        {
            int n = deflate.Read(pixels[read..]);
            if (n == 0)
                break;
            read += n;
        }

        if (read != pixels.Length)
        {
            bitmap.Dispose();
            throw new InvalidDataException($"Truncated reference blob {path}: read {read} of {pixels.Length} bytes.");
        }

        return bitmap;
    }
}

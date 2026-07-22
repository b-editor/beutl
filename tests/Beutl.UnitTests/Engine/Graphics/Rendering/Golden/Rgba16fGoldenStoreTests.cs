using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[TestFixture]
public sealed class Rgba16fGoldenStoreTests
{
    private string _temporaryDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"beutl-rgba16f-golden-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);
    }

    [Test]
    public void WriteAndRead_RoundTripsRawHalfBitsAndMetadata()
    {
        const int width = 3;
        const int height = 2;
        using var source = new Bitmap(
            width,
            height,
            BitmapColorType.RgbaF16,
            BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);

        for (int y = 0; y < height; y++)
        {
            Span<ushort> row = source.GetRow<ushort>(y);
            for (int i = 0; i < row.Length; i++)
            {
                row[i] = unchecked((ushort)(0x1000 + y * row.Length + i));
            }
        }

        string path = Path.Combine(_temporaryDirectory, "round-trip" + Rgba16fGoldenStore.Extension);
        Rgba16fGoldenStore.Write(path, source);
        using Bitmap restored = Rgba16fGoldenStore.Read(path, width, height);

        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(path).Length, Is.EqualTo(width * height * 8));
            Assert.That(restored.Width, Is.EqualTo(width));
            Assert.That(restored.Height, Is.EqualTo(height));
            Assert.That(restored.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(restored.AlphaType, Is.EqualTo(BitmapAlphaType.Premul));
            Assert.That(restored.ColorSpace, Is.EqualTo(BitmapColorSpace.LinearSrgb));
        });

        for (int y = 0; y < height; y++)
        {
            Assert.That(restored.GetRow<ushort>(y).ToArray(), Is.EqualTo(source.GetRow<ushort>(y).ToArray()));
        }
    }

    [Test]
    public void Write_UsesHeaderlessLittleEndianPayload_AndHashesCompleteBlob()
    {
        using var source = new Bitmap(
            1,
            1,
            BitmapColorType.RgbaF16,
            BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);
        Span<ushort> pixel = source.GetRow<ushort>(0);
        pixel[0] = BitConverter.HalfToUInt16Bits((Half)0.5f);
        pixel[1] = BitConverter.HalfToUInt16Bits((Half)0.25f);
        pixel[2] = BitConverter.HalfToUInt16Bits((Half)0f);
        pixel[3] = BitConverter.HalfToUInt16Bits((Half)1f);

        string path = Path.Combine(_temporaryDirectory, "canonical" + Rgba16fGoldenStore.Extension);
        Rgba16fGoldenStore.Write(path, source);

        Assert.Multiple(() =>
        {
            Assert.That(
                File.ReadAllBytes(path),
                Is.EqualTo(new byte[] { 0x00, 0x38, 0x00, 0x34, 0x00, 0x00, 0x00, 0x3c }));
            Assert.That(
                Rgba16fGoldenStore.ComputeSha256(path),
                Is.EqualTo("0def1baa18cbddd7a49b5460d10dd76b2131885086197380111c3e3ed51408a9"));
        });
    }

    [Test]
    public void Write_ExistingArtifactIsNeverReplaced()
    {
        using var original = CreateFlat(0.25f);
        using var replacement = CreateFlat(0.75f);
        string path = Path.Combine(_temporaryDirectory, "immutable" + Rgba16fGoldenStore.Extension);

        Rgba16fGoldenStore.Write(path, original);
        byte[] frozen = File.ReadAllBytes(path);

        Assert.That(() => Rgba16fGoldenStore.Write(path, replacement), Throws.InstanceOf<IOException>());
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(frozen));
        Assert.That(Directory.GetFiles(_temporaryDirectory, "*.tmp", SearchOption.TopDirectoryOnly), Is.Empty);
    }

    [Test]
    public void Read_RejectsLengthThatDoesNotMatchManifestDimensions()
    {
        using var source = CreateFlat(0.5f);
        string path = Path.Combine(_temporaryDirectory, "length" + Rgba16fGoldenStore.Extension);
        Rgba16fGoldenStore.Write(path, source);

        Assert.That(() => Rgba16fGoldenStore.Read(path, 2, 1), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Write_RejectsNoncanonicalBitmapMetadata()
    {
        using var unpremultiplied = new Bitmap(
            1,
            1,
            BitmapColorType.RgbaF16,
            BitmapAlphaType.Unpremul,
            BitmapColorSpace.LinearSrgb);
        string path = Path.Combine(_temporaryDirectory, "invalid" + Rgba16fGoldenStore.Extension);

        Assert.That(
            () => Rgba16fGoldenStore.Write(path, unpremultiplied),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("bitmap"));
        Assert.That(File.Exists(path), Is.False);
    }

    private static Bitmap CreateFlat(float value)
    {
        var bitmap = new Bitmap(
            1,
            1,
            BitmapColorType.RgbaF16,
            BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);
        bitmap.GetRow<ushort>(0).Fill(BitConverter.HalfToUInt16Bits((Half)value));
        return bitmap;
    }
}

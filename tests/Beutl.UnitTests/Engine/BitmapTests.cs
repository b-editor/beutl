using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class BitmapTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Save_PropagatesWriteFailuresWithoutTerminatingTheProcess(bool linear)
    {
        await TestWorkerProgram.RunAsync(TestWorkerProgram.BitmapSaveWorkerArgument, linear.ToString());
    }

    internal static void RunSaveFailureWorker(bool linear)
    {
        using var bitmap = new Bitmap(8, 8,
            colorSpace: linear ? BitmapColorSpace.LinearSrgb : BitmapColorSpace.Srgb);
        using var stream = new FailingWriteStream();
        Assert.Throws<IOException>(() => bitmap.Save(stream));
        Assert.That(stream.CanWrite, Is.True, "Save must not dispose the caller's stream.");
    }

    private sealed class FailingWriteStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("write failed");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("write failed");
    }

    [Test]
    public void Save_UnsupportedFormatPreservesThePreviousFile()
    {
        string root = Directory.CreateTempSubdirectory("beutl-bitmap-save-").FullName;
        try
        {
            string output = Path.Combine(root, "image.png");
            using var bitmap = new Bitmap(8, 8);
            Assert.That(bitmap.Save(output), Is.True);
            byte[] previous = File.ReadAllBytes(output);

            Assert.That(bitmap.Save(output, Beutl.Graphics.EncodedImageFormat.Gif), Is.False);

            Assert.That(File.ReadAllBytes(output), Is.EqualTo(previous));
            Assert.That(Directory.GetDirectories(root), Is.Empty);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Save_PreservesPrivatePermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix permissions are required.");
            return;
        }
        string root = Directory.CreateTempSubdirectory("beutl-bitmap-save-").FullName;
        try
        {
            string output = Path.Combine(root, "image.png");
            File.WriteAllText(output, "previous");
            File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            using var bitmap = new Bitmap(8, 8);

            Assert.That(bitmap.Save(output), Is.True);

            Assert.That(File.GetUnixFileMode(output), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            using var restored = Bitmap.FromFile(output);
            Assert.That(restored.Width, Is.EqualTo(8));
            Assert.That(Directory.GetDirectories(root), Is.Empty);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Constructor_BasicSize_PropertiesAreInitialized()
    {
        using var bitmap = new Bitmap(10, 20);
        Assert.That(bitmap.Width, Is.EqualTo(10));
        Assert.That(bitmap.Height, Is.EqualTo(20));
        Assert.That(bitmap.IsDisposed, Is.False);
        Assert.That(bitmap.ByteCount, Is.GreaterThan(0));
        Assert.That(bitmap.BytesPerPixel, Is.GreaterThan(0));
        Assert.That(bitmap.RowBytes, Is.GreaterThanOrEqualTo(bitmap.Width * bitmap.BytesPerPixel));
    }

    [Test]
    public void Constructor_NegativeSize_Throws()
    {
        Assert.That(() => new Bitmap(-1, 10), Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(() => new Bitmap(10, -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Info_ContainsAllProperties()
    {
        using var bitmap = new Bitmap(5, 6);
        BitmapInfo info = bitmap.Info;
        Assert.That(info.Width, Is.EqualTo(5));
        Assert.That(info.Height, Is.EqualTo(6));
        Assert.That(info.ByteCount, Is.EqualTo(bitmap.ByteCount));
    }

    [Test]
    public void GetPixelSpan_HasCorrectLength()
    {
        using var bitmap = new Bitmap(3, 4);
        Assert.That(bitmap.GetPixelSpan().Length, Is.EqualTo(bitmap.ByteCount));
    }

    [Test]
    public void GetPixelSpan_Generic_HasExpectedLength()
    {
        using var bitmap = new Bitmap(4, 5);
        var span = bitmap.GetPixelSpan<int>();
        Assert.That(span.Length * sizeof(int), Is.EqualTo(bitmap.ByteCount));
    }

    [Test]
    public void GetRow_ReturnsExpectedSize()
    {
        using var bitmap = new Bitmap(8, 4);
        Assert.That(bitmap.GetRow(0).Length, Is.EqualTo(bitmap.Width * bitmap.BytesPerPixel));
        Assert.That(bitmap.GetRow(3).Length, Is.EqualTo(bitmap.Width * bitmap.BytesPerPixel));
    }

    [Test]
    public void GetRow_Negative_Throws()
    {
        using var bitmap = new Bitmap(4, 4);
        Assert.That(() => bitmap.GetRow(-1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(() => bitmap.GetRow(4), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Clone_MakesIndependentCopy()
    {
        using var bitmap = new Bitmap(4, 4);
        using var copy = bitmap.Clone();
        Assert.That(copy.Width, Is.EqualTo(bitmap.Width));
        Assert.That(copy.Height, Is.EqualTo(bitmap.Height));
        Assert.That(copy, Is.Not.SameAs(bitmap));
    }

    [Test]
    public void Clear_DoesNotThrow()
    {
        using var bitmap = new Bitmap(4, 4);
        Assert.That(() => bitmap.Clear(), Throws.Nothing);
    }

    [Test]
    public void Dispose_SetsIsDisposed()
    {
        var bitmap = new Bitmap(4, 4);
        bitmap.Dispose();
        Assert.That(bitmap.IsDisposed, Is.True);
    }

    [Test]
    public void Operations_AfterDispose_Throw()
    {
        var bitmap = new Bitmap(4, 4);
        bitmap.Dispose();
        Assert.That(() => bitmap.GetPixelSpan(), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(() => bitmap.Clear(), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(() => bitmap.Clone(), Throws.InstanceOf<ObjectDisposedException>());
    }

    [Test]
    public void ICloneable_Clone_ReturnsBitmap()
    {
        using var bitmap = new Bitmap(2, 2);
        ICloneable cloneable = bitmap;
        using var copy = (Bitmap)cloneable.Clone();
        Assert.That(copy.Width, Is.EqualTo(bitmap.Width));
    }

    [Test]
    public void ExtractSubset_ReturnsCorrectSize()
    {
        using var bitmap = new Bitmap(10, 10);
        using var sub = bitmap.ExtractSubset(new PixelRect(2, 2, 4, 4));
        Assert.That(sub.Width, Is.EqualTo(4));
        Assert.That(sub.Height, Is.EqualTo(4));
    }

    [Test]
    public void ExtractSubset_OutOfRange_Throws()
    {
        using var bitmap = new Bitmap(4, 4);
        Assert.That(() => bitmap.ExtractSubset(new PixelRect(0, 0, 10, 10)),
            Throws.InstanceOf<ArgumentException>().Or.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ColorType_Default_IsBgra8888()
    {
        using var bitmap = new Bitmap(2, 2);
        Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.Bgra8888));
    }

    [Test]
    public void Convert_ToDifferentColorType_Succeeds()
    {
        using var bitmap = new Bitmap(2, 2);
        using var converted = bitmap.Convert(BitmapColorType.Rgba8888);
        Assert.That(converted.ColorType, Is.EqualTo(BitmapColorType.Rgba8888));
        Assert.That(converted.Width, Is.EqualTo(bitmap.Width));
    }
}

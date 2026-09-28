using Avalonia.Platform.Storage;
using Beutl.Media;
using Beutl.Views;
using Moq;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

public class FrameImageSaveTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory("beutl-save-frame-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void The_save_picker_only_offers_encodable_formats_while_open_keeps_decoder_formats()
    {
        Assert.That(SharedFilePickerOptions.SaveImage().FileTypeChoices!.Single().Patterns,
            Is.EquivalentTo(new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }));
        Assert.That(SharedFilePickerOptions.OpenImage().FileTypeFilter!.Single().Patterns, Does.Contain("*.bmp").And.Contain("*.gif"));
    }

    [Test]
    [TestCase("png", SKEncodedImageFormat.Png)]
    [TestCase("jpg", SKEncodedImageFormat.Jpeg)]
    [TestCase("JPEG", SKEncodedImageFormat.Jpeg)]
    [TestCase("webp", SKEncodedImageFormat.Webp)]
    public async Task Saving_replaces_the_file_with_a_complete_image(string extension, SKEncodedImageFormat format)
    {
        string path = Path.Combine(_root, "image." + extension);
        File.WriteAllBytes(path, new byte[16384]);
        var file = StorageFile(path);
        using var bitmap = new Bitmap(8, 6);

        await PlayerView.SaveImage(file.Object, bitmap);

        using var decoded = SKBitmap.Decode(path);
        using var codec = SKCodec.Create(path);
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.Width, Is.EqualTo(8));
        Assert.That(decoded.Height, Is.EqualTo(6));
        Assert.That(codec.EncodedFormat, Is.EqualTo(format));
        Assert.That(new FileInfo(path).Length, Is.LessThan(16384));
        Assert.That(Directory.GetDirectories(_root), Is.Empty);
    }

    [Test]
    [TestCase("bmp")]
    [TestCase("gif")]
    [TestCase("avif")]
    public void Unsupported_formats_are_rejected_before_touching_the_destination(string extension)
    {
        string path = Path.Combine(_root, "image." + extension);
        File.WriteAllText(path, "previous image");
        var file = StorageFile(path);
        using var bitmap = new Bitmap(8, 6);

        Assert.ThrowsAsync<NotSupportedException>(async () => await PlayerView.SaveImage(file.Object, bitmap));

        Assert.That(File.ReadAllText(path), Is.EqualTo("previous image"));
        Assert.That(Directory.GetDirectories(_root), Is.Empty);
        file.Verify(value => value.OpenWriteAsync(), Times.Never);
    }

    [Test]
    public void Encoder_failure_preserves_the_existing_image()
    {
        string path = Path.Combine(_root, "image.png");
        File.WriteAllText(path, "previous image");
        using var empty = new Bitmap(new SKBitmap());

        Assert.ThrowsAsync<IOException>(async () => await PlayerView.SaveImage(StorageFile(path).Object, empty));

        Assert.That(File.ReadAllText(path), Is.EqualTo("previous image"));
        Assert.That(Directory.GetDirectories(_root), Is.Empty);
    }

    [Test]
    public async Task An_opaque_storage_provider_receives_a_complete_image_without_old_trailing_bytes()
    {
        var file = new Mock<IStorageFile>();
        file.SetupGet(value => value.Name).Returns("image.png");
        file.SetupGet(value => value.Path).Returns(new Uri("content://storage/image.png"));
        var destination = new MemoryStream();
        destination.Write(new byte[16384]);
        file.Setup(value => value.OpenWriteAsync()).ReturnsAsync(destination);
        using var bitmap = new Bitmap(8, 6);

        await PlayerView.SaveImage(file.Object, bitmap);

        byte[] bytes = destination.ToArray();
        using var decoded = SKBitmap.Decode(bytes);
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.Width, Is.EqualTo(8));
        Assert.That(bytes.Length, Is.LessThan(16384));
        file.Verify(value => value.OpenWriteAsync(), Times.Once);
    }

    private static Mock<IStorageFile> StorageFile(string path)
    {
        var file = new Mock<IStorageFile>();
        file.SetupGet(value => value.Name).Returns(Path.GetFileName(path));
        file.SetupGet(value => value.Path).Returns(new Uri(path));
        return file;
    }
}

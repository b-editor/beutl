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
        FilePickerSaveOptions options = SharedFilePickerOptions.SaveImage();
        Assert.That(options.DefaultExtension, Is.EqualTo("png"));
        Assert.That(options.FileTypeChoices!.Single().Patterns,
            Is.EquivalentTo(new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }));
        Assert.That(SharedFilePickerOptions.OpenImage().FileTypeFilter!.Single().Patterns, Does.Contain("*.bmp").And.Contain("*.gif"));
    }

    [Test]
    public async Task The_image_picker_passes_png_defaults_and_suggested_name_to_storage()
    {
        var folder = new Mock<IStorageFolder>(MockBehavior.Strict);
        var file = new Mock<IStorageFile>(MockBehavior.Strict);
        var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
        FilePickerSaveOptions? captured = null;
        storage.Setup(value => value.TryGetWellKnownFolderAsync(WellKnownFolder.Pictures))
            .ReturnsAsync(folder.Object);
        storage.Setup(value => value.SaveFilePickerAsync(It.IsAny<FilePickerSaveOptions>()))
            .Callback<FilePickerSaveOptions>(options => captured = options)
            .ReturnsAsync(file.Object);

        IStorageFile? result = await PlayerView.SaveImageFilePicker("frame", storage.Object);

        Assert.That(captured, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(captured!.DefaultExtension, Is.EqualTo("png"));
            Assert.That(captured.SuggestedFileName, Does.StartWith("frame ").And.EndWith(".png"));
            Assert.That(captured.SuggestedStartLocation, Is.SameAs(folder.Object));
            Assert.That(captured.FileTypeChoices!.Single().Patterns,
                Is.EquivalentTo(new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }));
            Assert.That(result, Is.SameAs(file.Object));
        });
        storage.Verify(value => value.SaveFilePickerAsync(It.IsAny<FilePickerSaveOptions>()), Times.Once);
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
    public async Task Saving_preserves_private_image_permissions()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file permissions are required.");
            return;
        }

        string path = Path.Combine(_root, "private.png");
        File.WriteAllText(path, "previous image");
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(path, mode);
        using var bitmap = new Bitmap(8, 6);

        await PlayerView.SaveImage(StorageFile(path).Object, bitmap);

        Assert.That(File.GetUnixFileMode(path), Is.EqualTo(mode));
        using var decoded = SKBitmap.Decode(path);
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.Width, Is.EqualTo(8));
    }

    [Test]
    [TestCase("bmp")]
    [TestCase("gif")]
    [TestCase("avif")]
    public async Task Unsupported_formats_are_rejected_before_touching_the_destination(string extension)
    {
        string path = Path.Combine(_root, "image." + extension);
        File.WriteAllText(path, "previous image");
        var file = StorageFile(path);
        using var bitmap = new Bitmap(8, 6);

        await Assert.ThrowsAsync<NotSupportedException>(async () => await PlayerView.SaveImage(file.Object, bitmap));

        Assert.That(File.ReadAllText(path), Is.EqualTo("previous image"));
        Assert.That(Directory.GetDirectories(_root), Is.Empty);
        file.Verify(value => value.OpenWriteAsync(), Times.Never);
    }

    [Test]
    public async Task Encoder_failure_preserves_the_existing_image()
    {
        string path = Path.Combine(_root, "image.png");
        File.WriteAllText(path, "previous image");
        using var empty = new Bitmap(new SKBitmap());

        await Assert.ThrowsAsync<IOException>(async () => await PlayerView.SaveImage(StorageFile(path).Object, empty));

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

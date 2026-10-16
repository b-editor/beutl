using Beutl.Extensions.AVFoundation.Encoding;
using Beutl.Media;

namespace Beutl.Extensions.AVFoundation.Tests;

// The encoder writes MP4, QuickTime and M4V files, each in its own container. It used to write MP4 data under every
// extension it offered, such as .mov, .avi and .wmv.
[TestFixture]
[Platform("MacOSX")]
public class ExportContainerTests
{
    private string _workDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-avf-container-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Test]
    public void The_encoder_offers_only_the_containers_it_writes()
    {
        Assert.That(new AVFEncodingExtension().SupportExtensions(), Is.EqualTo(new[] { ".mp4", ".mov", ".m4v" }));
    }

    [TestCase(".mov", "qt  ")]
    [TestCase(".m4v", "M4V ")]
    [TestCase(".mp4", "mp42")]
    public async Task Each_container_gets_its_own_file_type(string extension, string majorBrand)
    {
        string outputPath = Path.Combine(_workDir, "clip" + extension);
        var controller = new AVFEncodingController(outputPath);
        var frameRate = new Rational(30, 1);
        controller.VideoSettings.DestinationSize = new PixelSize(64, 64);
        controller.VideoSettings.SourceSize = new PixelSize(64, 64);
        controller.VideoSettings.FrameRate = frameRate;
        controller.AudioSettings.SampleRate = 44100;
        controller.AudioSettings.Channels = 2;

        await controller.Encode(
            new GradientFrameProvider(3, frameRate, 64, 64),
            new SineSampleProvider(4410, 44100),
            CancellationToken.None);

        // The file starts with its ftyp box: a 4-byte size, "ftyp", then the major brand.
        byte[] header = new byte[12];
        await using (FileStream stream = File.OpenRead(outputPath))
        {
            await stream.ReadExactlyAsync(header);
        }

        Assert.Multiple(() =>
        {
            Assert.That(System.Text.Encoding.ASCII.GetString(header, 4, 4), Is.EqualTo("ftyp"));
            Assert.That(System.Text.Encoding.ASCII.GetString(header, 8, 4), Is.EqualTo(majorBrand));
        });
    }
}

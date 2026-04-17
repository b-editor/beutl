using Beutl.Extensions.AVFoundation.Decoding;
using Beutl.Extensions.AVFoundation.Encoding;
using Beutl.Media;
using Beutl.Media.Decoding;

namespace Beutl.Extensions.AVFoundation.Tests;

[TestFixture]
[Platform("MacOSX")]
public class EncodeDecodeRoundTripTests
{
    private string _workDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-avf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Test]
    public async Task EncodesThenDecodesShortClip()
    {
        string outputPath = Path.Combine(_workDir, "clip.mp4");
        var controller = new AVFEncodingController(outputPath);

        const int width = 64;
        const int height = 64;
        const int sampleRate = 44100;
        const int frameCount = 30;
        const int frameRateNum = 30;
        const int frameRateDen = 1;

        controller.VideoSettings.DestinationSize = new PixelSize(width, height);
        controller.VideoSettings.SourceSize = new PixelSize(width, height);
        controller.VideoSettings.FrameRate = new Rational(frameRateNum, frameRateDen);

        controller.AudioSettings.SampleRate = sampleRate;
        controller.AudioSettings.Channels = 2;

        var frameProvider = new GradientFrameProvider(
            frameCount, new Rational(frameRateNum, frameRateDen), width, height);
        var sampleProvider = new SineSampleProvider(sampleRate, sampleRate);

        await controller.Encode(frameProvider, sampleProvider, CancellationToken.None);

        Assert.That(File.Exists(outputPath), Is.True, "Encoder should produce an output file.");
        var fileInfo = new FileInfo(outputPath);
        Assert.That(fileInfo.Length, Is.GreaterThan(0), "Output file must contain data.");

        // Now decode the same file and verify basic stream metadata round-trips.
        var decodingExtension = new AVFDecodingExtension();
        using var reader = new AVFReader(outputPath, new MediaOptions(), decodingExtension);

        Assert.That(reader.HasVideo, Is.True, "Decoded file should expose a video track.");
        Assert.That(reader.VideoInfo.FrameSize.Width, Is.EqualTo(width));
        Assert.That(reader.VideoInfo.FrameSize.Height, Is.EqualTo(height));

        Assert.That(reader.HasAudio, Is.True, "Decoded file should expose an audio track.");
        Assert.That(reader.AudioInfo.SampleRate, Is.EqualTo(sampleRate));

        // Read a representative frame from mid-clip; VideoToolbox is lossy so we only
        // check the decode succeeded without asserting pixel equality.
        bool videoRead = reader.ReadVideo(frame: frameCount / 2, out var videoImage);
        Assert.That(videoRead, Is.True, "Must decode a mid-clip frame.");
        using (videoImage)
        {
            Assert.That(videoImage!.Value.Width, Is.EqualTo(width));
            Assert.That(videoImage.Value.Height, Is.EqualTo(height));
        }

        // Audio decode: read 1024 samples from the start and confirm the buffer matches the
        // requested layout.
        bool audioRead = reader.ReadAudio(start: 0, length: 1024, out var audioPcm);
        Assert.That(audioRead, Is.True, "Must decode the first audio chunk.");
        using (audioPcm)
        {
            Assert.That(audioPcm!.Value.SampleRate, Is.EqualTo(sampleRate));
            Assert.That(audioPcm.Value.NumSamples, Is.EqualTo(1024));
            Assert.That(audioPcm.Value.NumChannels, Is.EqualTo(2));
        }
    }
}

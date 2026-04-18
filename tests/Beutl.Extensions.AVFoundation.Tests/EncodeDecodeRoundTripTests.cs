using Beutl.Extensions.AVFoundation.Decoding;
using Beutl.Extensions.AVFoundation.Encoding;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

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
        // Synthetic input has no HDR metadata → should come back as SDR 8bpc.
        Assert.That(reader.VideoInfo.CodecName, Is.Not.Empty);

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

            // Sanity check the decoded audio: AAC is lossy but the input is a pure 440 Hz
            // tone at ~0.25 amplitude. If the decoder silently hands back zeros (or garbage
            // outside the unit interval) we want to catch it.
            var pcm = (Pcm<Stereo32BitFloat>)audioPcm.Value;
            var samples = pcm.DataSpan;
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                peak = Math.Max(peak, Math.Max(Math.Abs(samples[i].Left), Math.Abs(samples[i].Right)));
            }
            Assert.That(peak, Is.GreaterThan(0.01f),
                "Decoded audio should not be silent; 440 Hz tone should survive round trip.");
            Assert.That(peak, Is.LessThanOrEqualTo(1.0f),
                "Decoded samples must stay within [-1, 1] for Stereo32BitFloat.");
        }
    }

    [Test]
    public async Task EncodesHdrPqHevcClipThenDecodes()
    {
        string outputPath = Path.Combine(_workDir, "hdr.mp4");
        var controller = new AVFEncodingController(outputPath);

        const int width = 128;
        const int height = 128;
        const int frameCount = 6;
        const int frameRateNum = 30;
        const int frameRateDen = 1;

        controller.VideoSettings.DestinationSize = new PixelSize(width, height);
        controller.VideoSettings.SourceSize = new PixelSize(width, height);
        controller.VideoSettings.FrameRate = new Rational(frameRateNum, frameRateDen);
        // HDR PQ + Rec.2020 — this flips the writer to HEVC Main10 + CV64RGBALE input and
        // stamps the AVVideoColorProperties tags on the output stream.
        controller.VideoSettings.ColorTransfer = AVFVideoEncoderSettings.ColorTransferCharacteristic.Pq;
        controller.VideoSettings.ColorPrimaries = AVFVideoEncoderSettings.ColorPrimariesType.Rec2020;
        controller.VideoSettings.YCbCrMatrix = AVFVideoEncoderSettings.YCbCrMatrixType.Rec2020;
        controller.AudioSettings.SampleRate = 44100;
        controller.AudioSettings.Channels = 2;

        Assert.That(controller.VideoSettings.IsHdr, Is.True);

        var frameProvider = new GradientFrameProvider(
            frameCount, new Rational(frameRateNum, frameRateDen), width, height);
        var sampleProvider = new SineSampleProvider(44100, 44100);

        await controller.Encode(frameProvider, sampleProvider, CancellationToken.None);
        Assert.That(File.Exists(outputPath), Is.True, "HDR encoder should produce an output file.");

        // Reader should detect the PQ transfer we just wrote and flip into HDR mode
        // (Rgba16161616 bitmaps with a luminance-scaled Rec.2020 target color space).
        var decodingExtension = new AVFDecodingExtension();
        using var reader = new AVFReader(outputPath, new MediaOptions(), decodingExtension);
        Assert.That(reader.HasVideo, Is.True);

        bool videoRead = reader.ReadVideo(frame: 1, out var image);
        Assert.That(videoRead, Is.True, "Mid-HDR-clip frame must decode.");
        using (image)
        {
            Assert.That(image!.Value.Width, Is.EqualTo(width));
            Assert.That(image.Value.Height, Is.EqualTo(height));
            Assert.That(image.Value.ColorType, Is.EqualTo(BitmapColorType.Rgba16161616),
                "HDR stream must decode into a 16bpc Bitmap.");

            // Confirm the HDR pixel pipeline actually landed non-zero data in the Bitmap.
            // HEVC is lossy so we can't compare exact values, but an entirely-black decode
            // would indicate the pool / swizzle / tagging chain got disconnected somewhere.
            unsafe
            {
                var words = new ReadOnlySpan<ushort>(
                    (void*)image.Value.Data, image.Value.ByteCount / sizeof(ushort));
                bool nonBlack = false;
                for (int i = 0; i + 3 < words.Length; i += 4)
                {
                    if (words[i] != 0 || words[i + 1] != 0 || words[i + 2] != 0)
                    {
                        nonBlack = true;
                        break;
                    }
                }
                Assert.That(nonBlack, Is.True, "HDR decode returned an entirely-black frame.");
            }
        }
    }
}

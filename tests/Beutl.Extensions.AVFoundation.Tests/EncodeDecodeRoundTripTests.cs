using System.Runtime.InteropServices;
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

    [TestCase(AVFVideoEncoderSettings.ColorTransferCharacteristic.Default, AVFVideoEncoderSettings.YCbCrMatrixType.Default)]
    [TestCase(AVFVideoEncoderSettings.ColorTransferCharacteristic.Default, AVFVideoEncoderSettings.YCbCrMatrixType.Bt601)]
    [TestCase(AVFVideoEncoderSettings.ColorTransferCharacteristic.Srgb, AVFVideoEncoderSettings.YCbCrMatrixType.Bt709)]
    [TestCase(AVFVideoEncoderSettings.ColorTransferCharacteristic.Linear, AVFVideoEncoderSettings.YCbCrMatrixType.Bt709)]
    public async Task SdrColorTagsAndPixelsRoundTrip(
        AVFVideoEncoderSettings.ColorTransferCharacteristic transfer, AVFVideoEncoderSettings.YCbCrMatrixType matrix)
    {
        string outputPath = Path.Combine(_workDir, "colors.mp4");
        var controller = new AVFEncodingController(outputPath);
        controller.VideoSettings.SourceSize.Value = controller.VideoSettings.DestinationSize.Value =
            new PixelSize(ColorBarsFrameProvider.Width, ColorBarsFrameProvider.Height);
        controller.VideoSettings.FrameRate.Value = new Rational(30, 1);
        controller.VideoSettings.ColorTransfer.Value = transfer;
        controller.VideoSettings.YCbCrMatrix.Value = matrix;
        using var frames = new ColorBarsFrameProvider();
        using var samples = new SineSampleProvider(4410, 44100);
        await controller.Encode(frames, samples, CancellationToken.None);

        using var reader = new AVFReader(outputPath, new MediaOptions(), new AVFDecodingExtension());
        Assert.That(reader.ReadVideo(0, out var decoded), Is.True);
        using (decoded)
        {
            var expectedTransfer = transfer == AVFVideoEncoderSettings.ColorTransferCharacteristic.Default
                ? BitmapColorTransfer.Bt709 : (BitmapColorTransfer)transfer;
            var expectedColorSpace = BitmapColorSpaceMapping.BuildTargetColorSpace(expectedTransfer, BitmapColorPrimaries.Bt709);
            Assert.That(decoded!.Value.ColorSpace, Is.EqualTo(expectedColorSpace));
            // Compare in the encoded transfer domain, without amplifying quantization noise near linear black.
            using Bitmap actual = decoded.Value.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, expectedColorSpace);
            using Bitmap source = ColorBarsFrameProvider.CreateFrame();
            using Bitmap expected = source.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, expectedColorSpace);
            for (int patch = 0; patch < 6; patch++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    int offset = (patch * 32 + 16) * 4 + channel;
                    Assert.That(actual.GetRow(32)[offset], Is.EqualTo(expected.GetRow(32)[offset]).Within(5),
                        $"Patch {patch}, channel {channel}");
                }
            }
        }
        Assert.That(controller.VideoSettings.YCbCrMatrix.Value, Is.EqualTo(matrix));
        Assert.That(controller.VideoSettings.ColorTransfer.Value, Is.EqualTo(transfer));
    }

    // A provider without frames returns null on its first read; one audio block keeps the file non-empty.
    [Test]
    public async Task EncodesAClipWithoutFrames()
    {
        string outputPath = Path.Combine(_workDir, "empty.mp4");
        var controller = new AVFEncodingController(outputPath);
        var frameRate = new Rational(30, 1);
        controller.VideoSettings.DestinationSize.Value = new PixelSize(64, 64);
        controller.VideoSettings.SourceSize.Value = new PixelSize(64, 64);
        controller.VideoSettings.FrameRate.Value = frameRate;

        await controller.Encode(
            new GradientFrameProvider(0, frameRate, 64, 64), new SineSampleProvider(1024, 44100), CancellationToken.None);

        Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(0));
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

        controller.VideoSettings.DestinationSize.Value = new PixelSize(width, height);
        controller.VideoSettings.SourceSize.Value = new PixelSize(width, height);
        controller.VideoSettings.FrameRate.Value = new Rational(frameRateNum, frameRateDen);

        controller.AudioSettings.SampleRate.Value = sampleRate;
        controller.AudioSettings.Channels.Value = 2;

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
            Assert.That(videoImage.Value.ColorType, Is.EqualTo(BitmapColorType.Bgra8888));
        }

        // Audio decode: read 1024 samples from the start and confirm the buffer matches the
        // requested layout.
        bool audioRead = reader.ReadAudio(start: 0, length: 1024, out var audioPcm);
        Assert.That(audioRead, Is.True, "Must decode the first audio chunk.");
        using (audioPcm)
        {
            Assert.That(audioPcm!.SampleRate, Is.EqualTo(sampleRate));
            Assert.That(audioPcm.SampleCount, Is.EqualTo(1024));
            Assert.That(audioPcm.ChannelCount, Is.EqualTo(2));

            // Sanity check the decoded audio: AAC is lossy but the input is a pure 440 Hz
            // tone at ~0.25 amplitude. If the decoder silently hands back zeros (or garbage
            // outside the unit interval) we want to catch it.
            Span<float> left = audioPcm.GetChannelData(0);
            Span<float> right = audioPcm.GetChannelData(1);
            float peak = 0f;
            for (int i = 0; i < left.Length; i++)
            {
                peak = Math.Max(peak, Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
            }
            Assert.That(peak, Is.GreaterThan(0.01f),
                "Decoded audio should not be silent; 440 Hz tone should survive round trip.");
            Assert.That(peak, Is.LessThanOrEqualTo(1.0f),
                "Decoded samples must stay within [-1, 1].");
        }
    }

    // Regression test: chunks across the timeline must not be stale copies of an earlier buffer. Encode 2 s of a
    // sweeping tone and verify the chunks are non-silent and pairwise distinct.
    [Test]
    public async Task EncodedAudioPreservesTimeline()
    {
        string outputPath = Path.Combine(_workDir, "timeline.mp4");
        var controller = new AVFEncodingController(outputPath);

        const int width = 64;
        const int height = 64;
        const int sampleRate = 44100;
        const int durationSeconds = 2;
        const int sampleCount = sampleRate * durationSeconds;
        const int frameRateNum = 30;
        const int frameRateDen = 1;
        const int frameCount = frameRateNum * durationSeconds;
        const int chunkSize = 1024;

        controller.VideoSettings.DestinationSize.Value = new PixelSize(width, height);
        controller.VideoSettings.SourceSize.Value = new PixelSize(width, height);
        controller.VideoSettings.FrameRate.Value = new Rational(frameRateNum, frameRateDen);

        controller.AudioSettings.SampleRate.Value = sampleRate;
        controller.AudioSettings.Channels.Value = 2;

        var frameProvider = new GradientFrameProvider(
            frameCount, new Rational(frameRateNum, frameRateDen), width, height);
        // Sweeping tone so successive chunks contain genuinely different waveforms; a stale-buffer
        // bug shows up as later chunks being bitwise identical to an earlier one.
        var sampleProvider = new SweepSampleProvider(sampleCount, sampleRate);

        await controller.Encode(frameProvider, sampleProvider, CancellationToken.None);

        var decodingExtension = new AVFDecodingExtension();
        using var reader = new AVFReader(outputPath, new MediaOptions(), decodingExtension);
        Assert.That(reader.HasAudio, Is.True);
        Assert.That(reader.AudioInfo.SampleRate, Is.EqualTo(sampleRate));

        int[] probes =
        {
            chunkSize,                       // skip the first chunk; AAC priming usually leaves it quiet
            sampleCount / 4,
            sampleCount / 2,
            3 * sampleCount / 4,
            sampleCount - chunkSize * 2,
        };
        var probeBytes = new byte[probes.Length][];

        for (int p = 0; p < probes.Length; p++)
        {
            bool ok = reader.ReadAudio(start: probes[p], length: chunkSize, out var probe);
            Assert.That(ok, Is.True, $"Audio chunk at sample {probes[p]} must decode.");
            using (probe)
            {
                Span<float> left = probe!.GetChannelData(0);
                Span<float> right = probe.GetChannelData(1);
                float peak = 0f;
                for (int i = 0; i < left.Length; i++)
                {
                    peak = Math.Max(peak, Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
                }
                Assert.That(peak, Is.GreaterThan(0.01f),
                    $"Chunk at sample {probes[p]} should have audible energy.");

                probeBytes[p] = [.. MemoryMarshal.AsBytes(left), .. MemoryMarshal.AsBytes(right)];
            }
        }

        for (int i = 0; i < probes.Length; i++)
        {
            for (int j = i + 1; j < probes.Length; j++)
            {
                Assert.That(probeBytes[i].AsSpan().SequenceEqual(probeBytes[j]), Is.False,
                    $"Chunks at samples {probes[i]} and {probes[j]} must not be bitwise identical.");
            }
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

        controller.VideoSettings.DestinationSize.Value = new PixelSize(width, height);
        controller.VideoSettings.SourceSize.Value = new PixelSize(width, height);
        controller.VideoSettings.FrameRate.Value = new Rational(frameRateNum, frameRateDen);
        // HDR PQ + Rec.2020 — this flips the writer to HEVC Main10 + CV64RGBALE input and
        // stamps the AVVideoColorProperties tags on the output stream.
        controller.VideoSettings.ColorTransfer.Value = AVFVideoEncoderSettings.ColorTransferCharacteristic.Pq;
        controller.VideoSettings.ColorPrimaries.Value = AVFVideoEncoderSettings.ColorPrimariesType.Rec2020;
        controller.VideoSettings.YCbCrMatrix.Value = AVFVideoEncoderSettings.YCbCrMatrixType.Rec2020;
        controller.AudioSettings.SampleRate.Value = 44100;
        controller.AudioSettings.Channels.Value = 2;

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

    // A provider without samples returns null on its first read.
    [Test]
    public async Task EncodesAClipWithoutSamples()
    {
        string outputPath = Path.Combine(_workDir, "silent.mp4");
        var controller = new AVFEncodingController(outputPath);
        var frameRate = new Rational(30, 1);
        controller.VideoSettings.DestinationSize.Value = new PixelSize(64, 64);
        controller.VideoSettings.SourceSize.Value = new PixelSize(64, 64);
        controller.VideoSettings.FrameRate.Value = frameRate;

        await controller.Encode(
            new GradientFrameProvider(1, frameRate, 64, 64), new SineSampleProvider(0, 44100), CancellationToken.None);

        Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(0));
    }
}

using Beutl.Audio;
using Beutl.Extensibility;
using Beutl.FFmpegWorker.Encoding;
using Beutl.Media;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class EncodingOutputFormatTests
{
    private string _workDir = string.Empty;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        FFmpegTestHelpers.RequireFFmpeg();
    }

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-ffmpeg-format-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_workDir, recursive: true);
    }

    [TestCase("unknown.beutl-unrecognized-format", false)]
    [TestCase("unknown.beutl-unrecognized-format", true)]
    [TestCase("missing-extension", false)]
    [TestCase("missing-extension", true)]
    public async Task Encode_UnrecognizedOutputFormat_FailsBeforeOpeningOutputFile(string fileName, bool existingOutput)
    {
        string outputPath = Path.Combine(_workDir, fileName);
        byte[] originalContents = [0x42, 0x65, 0x75, 0x74, 0x6c];
        if (existingOutput)
        {
            File.WriteAllBytes(outputPath, originalContents);
        }

        var controller = new FFmpegEncodingController(outputPath, new FFmpegEncodingSettings());
        using var frameProvider = new UnusedFrameProvider();
        using var sampleProvider = new UnusedSampleProvider();

        // GuessFormat can return a non-null wrapper around a null native format; Encode must reject it
        // before it opens the output file.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await controller.Encode(frameProvider, sampleProvider, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.StartWith("Could not determine FFmpeg output format from the file extension:"));
            Assert.That(File.Exists(outputPath), Is.EqualTo(existingOutput));
            if (existingOutput)
            {
                Assert.That(File.ReadAllBytes(outputPath), Is.EqualTo(originalContents));
            }
        });
    }

    [TestCase("audio.mp3", AVCodecID.AV_CODEC_ID_MP3)]
    [TestCase("audio.wav", AVCodecID.AV_CODEC_ID_PCM_S16LE)]
    public async Task Encode_WithoutFrames_WritesOnlyAudio(string fileName, AVCodecID audioCodec)
    {
        if (MediaCodec.FindEncoder(audioCodec) == null)
            Assert.Ignore($"This FFmpeg build cannot encode {audioCodec}.");

        string outputPath = Path.Combine(_workDir, fileName);
        var controller = new FFmpegEncodingController(outputPath, new FFmpegEncodingSettings());
        // Setting up a video stream fails at this size, so the export only succeeds without one.
        controller.VideoSettings.DestinationSize.Value = default;
        controller.AudioSettings.Format.Value = FFmpegAudioEncoderSettings.AudioFormat.Default;
        using var frames = new GradientFrameProvider(0, new Rational(30), 32, 32);
        using var samples = new SineSampleProvider(44100, 44100);

        await controller.Encode(frames, samples, CancellationToken.None);

        using var demuxer = MediaDemuxer.Open(outputPath);
        Assert.That(demuxer.Select(stream => stream.CodecparRef.codec_type),
            Is.EqualTo(new[] { AVMediaType.AVMEDIA_TYPE_AUDIO }));
    }

    private sealed class UnusedFrameProvider : IFrameProvider
    {
        public long FrameCount => throw new AssertionException("Invalid output formats must be rejected before reading frames.");

        public Rational FrameRate => throw new AssertionException("Invalid output formats must be rejected before reading frames.");

        public ValueTask<Bitmap?> ReadNextFrame() =>
            throw new AssertionException("Invalid output formats must be rejected before rendering frames.");

        public void Dispose()
        {
        }
    }

    private sealed class UnusedSampleProvider : ISampleProvider
    {
        public AudioChannelLayout ChannelLayout => AudioChannelLayout.Stereo;

        public long SampleCount => throw new AssertionException("Invalid output formats must be rejected before reading samples.");

        public long SampleRate => throw new AssertionException("Invalid output formats must be rejected before reading samples.");

        public ValueTask<AudioBuffer?> ReadNextSamples(int length) =>
            throw new AssertionException("Invalid output formats must be rejected before reading samples.");

        public void Dispose()
        {
        }
    }
}

using Beutl.FFmpegWorker.Encoding;
using Beutl.Media;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

// New video settings start with H.264 options. Only an H.264 encoder gets them, and only the ones it parses (#2898,
// #3434, #3466).
[TestFixture]
public sealed class EncoderDefaultOptionsTests
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
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-ffmpeg-options-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_workDir, recursive: true);
    }

    [Test]
    public async Task The_default_video_options_export_a_webm()
    {
        string outputPath = Path.Combine(_workDir, "default.webm");
        RequireDefaultEncoders(outputPath);
        // libopus takes neither 44.1 kHz nor fltp
        var controller = FFmpegTestHelpers.CreateController(
            outputPath, new PixelSize(64, 64), new Rational(30, 1), sampleRate: 48000, channels: 2);
        controller.AudioSettings.Format = FFmpegAudioEncoderSettings.AudioFormat.Default;
        using var frames = new GradientFrameProvider(3, new Rational(30, 1), 64, 64);
        using var samples = new SineSampleProvider(4800, 48000);

        await controller.Encode(frames, samples, CancellationToken.None);

        Assert.That(ReadVideoParameters(outputPath).codec_id, Is.EqualTo(AVCodecID.AV_CODEC_ID_VP9));
    }

    [Test]
    public async Task An_H264_export_keeps_its_saved_options()
    {
        string outputPath = Path.Combine(_workDir, "saved.mp4");
        RequireDefaultEncoders(outputPath);
        var controller = FFmpegTestHelpers.CreateController(
            outputPath, new PixelSize(64, 64), new Rational(30, 1), sampleRate: 44100, channels: 2);
        controller.VideoSettings.Options =
        [
            new("preset", "medium"),
            new("crf", "22"),
            new("profile", "high"),
            new("level", "4.0")
        ];
        using var frames = new GradientFrameProvider(3, new Rational(30, 1), 64, 64);
        using var samples = new SineSampleProvider(4410, 44100);

        await controller.Encode(frames, samples, CancellationToken.None);

        AVCodecParameters video = ReadVideoParameters(outputPath);
        Assert.Multiple(() =>
        {
            Assert.That(video.codec_id, Is.EqualTo(AVCodecID.AV_CODEC_ID_H264));
            // Without level=4.0, x264 gives this frame size level 1.0
            Assert.That(video.level, Is.EqualTo(40));
        });
    }

    // mpeg2video takes no level without a profile, so the default level 4.0 alone kept it from opening (#3466).
    [Test]
    public async Task The_default_video_options_export_an_mpeg2_video()
    {
        string outputPath = Path.Combine(_workDir, "default.mpg");
        RequireDefaultEncoders(outputPath);
        if (MediaCodec.FindEncoder("mpeg2video") == null) Assert.Ignore("This FFmpeg build has no mpeg2video encoder.");
        var controller = FFmpegTestHelpers.CreateController(
            outputPath, new PixelSize(64, 64), new Rational(30, 1), sampleRate: 44100, channels: 2);
        controller.VideoSettings.Codec = new CodecRecord("mpeg2video", "MPEG-2 video");
        controller.AudioSettings.Format = FFmpegAudioEncoderSettings.AudioFormat.Default;
        using var frames = new GradientFrameProvider(3, new Rational(30, 1), 64, 64);
        using var samples = new SineSampleProvider(4410, 44100);

        await controller.Encode(frames, samples, CancellationToken.None);

        Assert.That(ReadVideoParameters(outputPath).codec_id, Is.EqualTo(AVCodecID.AV_CODEC_ID_MPEG2VIDEO));
    }

    // h264_amf has no medium preset. Its option table is in every FFmpeg build with AMF, so no AMD GPU is needed.
    [TestCase("libx264", new[] { "preset", "crf", "profile", "level" })]
    [TestCase("h264_amf", new[] { "crf", "profile", "level" })]
    [TestCase("libx265", new[] { "crf" })]
    [TestCase("libvpx-vp9", new[] { "crf" })]
    [TestCase("libsvtav1", new[] { "crf" })]
    public void An_encoder_gets_only_the_default_options_it_takes(string encoder, string[] expected)
    {
        MediaCodec? codec = MediaCodec.FindEncoder(encoder);
        if (codec == null) Assert.Ignore($"This FFmpeg build has no {encoder} encoder.");

        IEnumerable<KeyValuePair<string, string>> options =
            FFmpegEncodingController.SelectVideoOptions(codec, new FFmpegVideoEncoderSettings().Options);

        Assert.That(options.Select(option => option.Key), Is.EqualTo(expected));
    }

    // A value the user typed is passed as it is, so a typing error still fails the export instead of being skipped.
    [TestCase("h264_amf", "preset", "quality")]
    [TestCase("libx265", "level", "5.1")]
    [TestCase("libx265", "profile", "main10")]
    public void A_value_other_than_the_default_reaches_the_encoder_as_it_is(string encoder, string name, string value)
    {
        MediaCodec? codec = MediaCodec.FindEncoder(encoder);
        if (codec == null) Assert.Ignore($"This FFmpeg build has no {encoder} encoder.");

        IEnumerable<KeyValuePair<string, string>> options =
            FFmpegEncodingController.SelectVideoOptions(codec, [new AdditionalOption(name, value)]);

        Assert.That(options, Is.EqualTo(new[] { KeyValuePair.Create(name, value) }));
    }

    private static void RequireDefaultEncoders(string outputPath)
    {
        OutputFormat format = OutputFormat.GuessFormat(null, outputPath, null);
        if (MediaCodec.FindEncoder(format.VideoCodec) == null || MediaCodec.FindEncoder(format.AudioCodec) == null)
            Assert.Ignore($"This FFmpeg build has no default encoders for {Path.GetExtension(outputPath)}.");
    }

    private static AVCodecParameters ReadVideoParameters(string path)
    {
        using MediaDemuxer demuxer = MediaDemuxer.Open(path);
        return demuxer.Single(s => s.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO).CodecparRef;
    }
}

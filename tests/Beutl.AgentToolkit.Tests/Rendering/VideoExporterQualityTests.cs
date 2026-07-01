using Beutl.AgentToolkit.Rendering;
using Beutl.Extensions.FFmpeg.Encoding;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class VideoExporterQualityTests
{
    [Test]
    public void Crf_overrides_the_crf_option()
    {
        var settings = new FFmpegVideoEncoderSettings();

        VideoExporter.ApplyQualitySettings(settings, crf: 28, bitrate: null);

        AdditionalOption crf = settings.Options.Single(option => option.Name == "crf");
        Assert.That(crf.Value, Is.EqualTo("28"));
    }

    [Test]
    public void Bitrate_sets_bitrate_and_drops_crf_option()
    {
        var settings = new FFmpegVideoEncoderSettings();

        VideoExporter.ApplyQualitySettings(settings, crf: null, bitrate: 4_000_000);

        Assert.Multiple(() =>
        {
            Assert.That(settings.Bitrate, Is.EqualTo(4_000_000));
            Assert.That(settings.Options.Any(option => option.Name == "crf"), Is.False);
        });
    }

    [Test]
    public void No_quality_arguments_leave_defaults_unchanged()
    {
        var settings = new FFmpegVideoEncoderSettings();
        int defaultBitrate = settings.Bitrate;

        VideoExporter.ApplyQualitySettings(settings, crf: null, bitrate: null);

        Assert.Multiple(() =>
        {
            Assert.That(settings.Bitrate, Is.EqualTo(defaultBitrate));
            Assert.That(settings.Options.Single(option => option.Name == "crf").Value, Is.EqualTo("22"));
        });
    }
}

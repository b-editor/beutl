using System.Reflection;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Tools;
using Beutl.Extensions.AVFoundation.Encoding;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class VideoExporterQualityTests
{
    [Test]
    public void RequiresFFmpegWorker_is_true_for_an_ffmpeg_only_container()
    {
        var exporter = new VideoExporter(new EncoderRegistration());

        // .webm is served only by the FFmpeg encoder on every platform.
        Assert.That(exporter.RequiresFFmpegWorker("out.webm"), Is.True);
    }

    [Test]
    public void RequiresFFmpegWorker_is_false_for_an_unregistered_container()
    {
        var exporter = new VideoExporter(new EncoderRegistration());

        Assert.That(exporter.RequiresFFmpegWorker("out.xyz"), Is.False);
    }

    [Test]
    public void RequiresFFmpegWorker_is_false_on_macos_for_avfoundation_containers()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Ignore("AVFoundation is only registered on macOS.");
        }

        var exporter = new VideoExporter(new EncoderRegistration());

        Assert.Multiple(() =>
        {
            Assert.That(exporter.RequiresFFmpegWorker("out.mp4"), Is.False);
            Assert.That(exporter.RequiresFFmpegWorker("out.mov"), Is.False);
        });
    }

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

    [Test]
    public void Export_settings_tag_ffmpeg_sdr_as_bt709_limited()
    {
        var settings = new FFmpegVideoEncoderSettings();

        VideoExporter.ApplyQualitySettings(settings, crf: null, bitrate: null);

        Assert.Multiple(() =>
        {
            Assert.That(settings.ColorPrimaries, Is.EqualTo(FFColorPrimaries.BT709));
            Assert.That(settings.ColorTrc, Is.EqualTo(FFColorTransfer.BT709));
            Assert.That(settings.ColorSpace, Is.EqualTo(FFColorSpace.BT709));
            Assert.That(settings.ColorRange, Is.EqualTo(FFColorRange.MPEG));
        });
    }

    [Test]
    public void Export_settings_tag_avfoundation_sdr_as_bt709()
    {
        var settings = new AVFVideoEncoderSettings();

        VideoExporter.ApplyQualitySettings(settings, crf: null, bitrate: null);

        Assert.Multiple(() =>
        {
            Assert.That(
                settings.ColorPrimaries,
                Is.EqualTo(AVFVideoEncoderSettings.ColorPrimariesType.Bt709));
            Assert.That(
                settings.ColorTransfer,
                Is.EqualTo(AVFVideoEncoderSettings.ColorTransferCharacteristic.Bt709));
            Assert.That(
                settings.YCbCrMatrix,
                Is.EqualTo(AVFVideoEncoderSettings.YCbCrMatrixType.Bt709));
        });
    }

    [Test]
    public void Crf_on_avfoundation_returns_an_explicit_ignored_parameter_warning()
    {
        var settings = new AVFVideoEncoderSettings();

        IReadOnlyList<string> warnings =
            VideoExporter.ApplyQualitySettings(settings, crf: 28, bitrate: null);

        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("AVFoundation").And.Contain("crf").And.Contain("ignored"));
    }

    [Test]
    public void Crf_tool_schema_documents_the_avfoundation_warning()
    {
        ParameterInfo crf = typeof(RenderTools)
            .GetMethod(nameof(RenderTools.ExportVideo))!
            .GetParameters()
            .Single(parameter => parameter.Name == "crf");
        string? description =
            crf.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description;

        Assert.That(
            description,
            Does.Contain("AVFoundation").And.Contain("warning"));
    }

    [Test]
    public void Bitrate_tool_schema_documents_the_avfoundation_undershoot_warning()
    {
        ParameterInfo bitrate = typeof(RenderTools)
            .GetMethod(nameof(RenderTools.ExportVideo))!
            .GetParameters()
            .Single(parameter => parameter.Name == "bitrate");
        string? description =
            bitrate.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description;

        Assert.That(
            description,
            Does.Contain("AVFoundation")
                .And.Contain("warning")
                .And.Contain("50%")
                .And.Contain("audio")
                .And.Contain("muxing"));
    }

    [Test]
    public void Avfoundation_bitrate_measurement_warns_below_half_the_request()
    {
        string outputPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"{Guid.NewGuid():N}.mp4");
        try
        {
            using (FileStream stream = File.Create(outputPath))
            {
                stream.SetLength(100_000);
            }

            string? warning = VideoExporter.CreateAvFoundationBitrateWarning(
                outputPath,
                TimeSpan.FromSeconds(2),
                requestedBitrate: 4_000_000);

            Assert.That(
                warning,
                Does.Contain("AVFoundation")
                    .And.Contain("400000")
                    .And.Contain("4000000")
                    .And.Contain("below 50%")
                    .And.Contain("container-wide approximation")
                    .And.Contain("audio")
                    .And.Contain("muxing"));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Test]
    public void Avfoundation_bitrate_measurement_subtracts_known_audio_rate()
    {
        string outputPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"{Guid.NewGuid():N}.mp4");
        try
        {
            using (FileStream stream = File.Create(outputPath))
            {
                // 525,000 bytes over two seconds is a 2.1 Mbit/s container rate.
                stream.SetLength(525_000);
            }

            string? warning = VideoExporter.CreateAvFoundationBitrateWarning(
                outputPath,
                TimeSpan.FromSeconds(2),
                requestedBitrate: 4_000_000,
                configuredAudioBitrate: 400_000);

            Assert.That(
                warning,
                Does.Contain("estimated average video bitrate")
                    .And.Contain("1700000")
                    .And.Contain("400000")
                    .And.Contain("2100000")
                    .And.Contain("below 50%"));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
}

using System;
using Beutl.Embedding.FFmpeg.Decoding;
using Beutl.Embedding.FFmpeg.Encoding;

namespace Beutl.UnitTests.Extensibility;

public class ExtensionSettingsEventsTests
{
    [Test]
    public void FFmpegDecodingSettings_RaisesConfigurationChanged_OnPropertySet()
    {
        var cfg = new FFmpegDecodingSettings();
        int changed = 0;
        cfg.ConfigurationChanged += (_, _) => changed++;

        cfg.ThreadCount = cfg.ThreadCount == -1 ? 2 : -1;
        cfg.Scaling = cfg.Scaling == FFmpegDecodingSettings.ScalingAlgorithm.Bicubic
            ? FFmpegDecodingSettings.ScalingAlgorithm.Bilinear
            : FFmpegDecodingSettings.ScalingAlgorithm.Bicubic;

        Assert.That(changed, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void FFmpegEncodingSettings_RaisesConfigurationChanged_OnPropertySet()
    {
        var cfg = new FFmpegEncodingSettings();
        int changed = 0;
        cfg.ConfigurationChanged += (_, _) => changed++;

        cfg.ThreadCount = cfg.ThreadCount == -1 ? 2 : -1;
        cfg.Acceleration = cfg.Acceleration == FFmpegEncodingSettings.AccelerationOptions.Software
            ? FFmpegEncodingSettings.AccelerationOptions.Auto
            : FFmpegEncodingSettings.AccelerationOptions.Software;

        Assert.That(changed, Is.GreaterThanOrEqualTo(2));
    }
}

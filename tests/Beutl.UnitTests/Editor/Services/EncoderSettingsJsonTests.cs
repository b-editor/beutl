using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Encoding;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class EncoderSettingsJsonTests
{
    [Test]
    public void Serialize_NullSettings_ReturnsNull()
    {
        Assert.That(EncoderSettingsJson.Serialize(null), Is.Null);
    }


    [Test]
    public void PopulateVideoPreset_PreservesTimelineOwnedSettings()
    {
        var preset = new VideoEncoderSettings
        {
            SourceSize = { Value = new PixelSize(640, 360) },
            DestinationSize = { Value = new PixelSize(640, 360) },
            FrameRate = { Value = new Rational(24, 1) },
            Bitrate = { Value = 2_000_000 },
            KeyframeRate = { Value = 8 }
        };
        var target = new VideoEncoderSettings
        {
            SourceSize = { Value = new PixelSize(3840, 2160) },
            DestinationSize = { Value = new PixelSize(1920, 1080) },
            FrameRate = { Value = new Rational(60, 1) },
            Bitrate = { Value = 5_000_000 },
            KeyframeRate = { Value = 12 }
        };

        EncoderSettingsJson.PopulateVideoPreset(
            target, EncoderSettingsJson.Serialize(preset)!, appliesFrameSizeAndRate: false);

        Assert.Multiple(() =>
        {
            Assert.That(target.SourceSize.Value, Is.EqualTo(new PixelSize(3840, 2160)));
            Assert.That(target.DestinationSize.Value, Is.EqualTo(new PixelSize(1920, 1080)));
            Assert.That(target.FrameRate.Value, Is.EqualTo(new Rational(60, 1)));
            Assert.That(target.Bitrate.Value, Is.EqualTo(preset.Bitrate.Value));
            Assert.That(target.KeyframeRate.Value, Is.EqualTo(preset.KeyframeRate.Value));
        });
    }

    [Test]
    public void PopulateVideoPreset_WhenPopulateThrows_PreservesTimelineOwnedSettings()
    {
        var preset = new VideoEncoderSettings
        {
            SourceSize = { Value = new PixelSize(640, 360) },
            DestinationSize = { Value = new PixelSize(640, 360) },
            FrameRate = { Value = new Rational(24, 1) },
            Bitrate = { Value = 2_000_000 }
        };
        JsonObject json = EncoderSettingsJson.Serialize(preset)!;
        json[nameof(VideoEncoderSettings.Bitrate)] = JsonValue.Create("invalid");
        var target = new VideoEncoderSettings
        {
            SourceSize = { Value = new PixelSize(3840, 2160) },
            DestinationSize = { Value = new PixelSize(1920, 1080) },
            FrameRate = { Value = new Rational(60, 1) },
            Bitrate = { Value = 5_000_000 }
        };

        Assert.That(
            () => EncoderSettingsJson.PopulateVideoPreset(target, json, appliesFrameSizeAndRate: false),
            Throws.Exception);

        Assert.Multiple(() =>
        {
            Assert.That(target.SourceSize.Value, Is.EqualTo(new PixelSize(3840, 2160)));
            Assert.That(target.DestinationSize.Value, Is.EqualTo(new PixelSize(1920, 1080)));
            Assert.That(target.FrameRate.Value, Is.EqualTo(new Rational(60, 1)));
        });
    }

    // A platform preset is named after what it applies: "YouTube 4K 60fps" applied to a 1920x1080, 30 fps output has
    // to export 3840x2160 at 60 fps, with the two-second keyframe interval it was built for.
    [Test]
    public void PopulateVideoPreset_ThatAppliesFrameSizeAndRate_AppliesThemAndKeepsTheSourceSize()
    {
        var preset = new VideoEncoderSettings
        {
            SourceSize = { Value = new PixelSize(3840, 2160) },
            DestinationSize = { Value = new PixelSize(3840, 2160) },
            FrameRate = { Value = new Rational(60, 1) },
            Bitrate = { Value = 45_000_000 },
            KeyframeRate = { Value = 120 }
        };
        var target = new VideoEncoderSettings
        {
            SourceSize = { Value = new PixelSize(1920, 1080) },
            DestinationSize = { Value = new PixelSize(1920, 1080) },
            FrameRate = { Value = new Rational(30, 1) },
            Bitrate = { Value = 5_000_000 },
            KeyframeRate = { Value = 12 }
        };

        EncoderSettingsJson.PopulateVideoPreset(
            target, EncoderSettingsJson.Serialize(preset)!, appliesFrameSizeAndRate: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.SourceSize.Value, Is.EqualTo(new PixelSize(1920, 1080)));
            Assert.That(target.DestinationSize.Value, Is.EqualTo(new PixelSize(3840, 2160)));
            Assert.That(target.FrameRate.Value, Is.EqualTo(new Rational(60, 1)));
            Assert.That(target.KeyframeRate.Value, Is.EqualTo(120));
            Assert.That(target.Bitrate.Value, Is.EqualTo(preset.Bitrate.Value));
        });
    }

    [Test]
    public void PopulateAudioPreset_ThatAppliesSampleRate_AppliesIt()
    {
        var preset = new AudioEncoderSettings
        {
            SampleRate = { Value = 48000 },
            Channels = { Value = 1 },
            Bitrate = { Value = 96_000 }
        };
        var target = new AudioEncoderSettings
        {
            SampleRate = { Value = 44100 },
            Channels = { Value = 2 },
            Bitrate = { Value = 128_000 }
        };

        EncoderSettingsJson.PopulateAudioPreset(target, EncoderSettingsJson.Serialize(preset)!, appliesSampleRate: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.SampleRate.Value, Is.EqualTo(48000));
            Assert.That(target.Channels.Value, Is.EqualTo(preset.Channels.Value));
            Assert.That(target.Bitrate.Value, Is.EqualTo(preset.Bitrate.Value));
        });
    }

    // Bitrate is read after the output size and the frame rate, so a preset that fails there has already written
    // both; a preset that does not apply leaves them as they were.
    [Test]
    public void PopulateVideoPreset_ThatAppliesFrameSizeAndRate_WhenPopulateThrows_KeepsThem()
    {
        var preset = new VideoEncoderSettings
        {
            DestinationSize = { Value = new PixelSize(3840, 2160) },
            FrameRate = { Value = new Rational(60, 1) },
        };
        JsonObject json = EncoderSettingsJson.Serialize(preset)!;
        json[nameof(VideoEncoderSettings.Bitrate)] = JsonValue.Create("invalid");
        var target = new VideoEncoderSettings
        {
            SourceSize = { Value = new PixelSize(1920, 1080) },
            DestinationSize = { Value = new PixelSize(1920, 1080) },
            FrameRate = { Value = new Rational(30, 1) },
        };

        Assert.That(
            () => EncoderSettingsJson.PopulateVideoPreset(target, json, appliesFrameSizeAndRate: true),
            Throws.Exception);

        Assert.Multiple(() =>
        {
            Assert.That(target.SourceSize.Value, Is.EqualTo(new PixelSize(1920, 1080)));
            Assert.That(target.DestinationSize.Value, Is.EqualTo(new PixelSize(1920, 1080)));
            Assert.That(target.FrameRate.Value, Is.EqualTo(new Rational(30, 1)));
        });
    }

    [Test]
    public void PopulateAudioPreset_ThatAppliesSampleRate_WhenPopulateThrows_KeepsIt()
    {
        JsonObject json = EncoderSettingsJson.Serialize(new AudioEncoderSettings { SampleRate = { Value = 48000 } })!;
        json[nameof(AudioEncoderSettings.Channels)] = JsonValue.Create("invalid");
        var target = new AudioEncoderSettings { SampleRate = { Value = 44100 } };

        Assert.That(
            () => EncoderSettingsJson.PopulateAudioPreset(target, json, appliesSampleRate: true),
            Throws.Exception);

        Assert.That(target.SampleRate.Value, Is.EqualTo(44100));
    }

    [Test]
    public void PopulateAudioPreset_PreservesSampleRate()
    {
        var preset = new AudioEncoderSettings
        {
            SampleRate = { Value = 48000 },
            Channels = { Value = 1 },
            Bitrate = { Value = 96_000 }
        };
        var target = new AudioEncoderSettings
        {
            SampleRate = { Value = 44100 },
            Channels = { Value = 2 },
            Bitrate = { Value = 128_000 }
        };

        EncoderSettingsJson.PopulateAudioPreset(
            target, EncoderSettingsJson.Serialize(preset)!, appliesSampleRate: false);

        Assert.Multiple(() =>
        {
            Assert.That(target.SampleRate.Value, Is.EqualTo(44100));
            Assert.That(target.Channels.Value, Is.EqualTo(preset.Channels.Value));
            Assert.That(target.Bitrate.Value, Is.EqualTo(preset.Bitrate.Value));
        });
    }

    [Test]
    public void PopulateAudioPreset_WhenPopulateThrows_PreservesSampleRate()
    {
        var preset = new AudioEncoderSettings
        {
            SampleRate = { Value = 48000 },
            Channels = { Value = 1 },
            Bitrate = { Value = 96_000 }
        };
        JsonObject json = EncoderSettingsJson.Serialize(preset)!;
        json[nameof(AudioEncoderSettings.Channels)] = JsonValue.Create("invalid");
        var target = new AudioEncoderSettings
        {
            SampleRate = { Value = 44100 },
            Channels = { Value = 2 },
            Bitrate = { Value = 128_000 }
        };

        Assert.That(
            () => EncoderSettingsJson.PopulateAudioPreset(target, json, appliesSampleRate: false),
            Throws.Exception);

        Assert.That(target.SampleRate.Value, Is.EqualTo(44100));
    }

    [Test]
    public void CopyTo_CopiesAllSettings()
    {
        var source = new VideoEncoderSettings
        {
            SourceSize = { Value = new PixelSize(1920, 1080) },
            DestinationSize = { Value = new PixelSize(1280, 720) },
            FrameRate = { Value = new Rational(60, 1) },
            Bitrate = { Value = 7_000_000 },
            KeyframeRate = { Value = 24 }
        };
        var destination = new VideoEncoderSettings();

        EncoderSettingsJson.CopyTo(source, destination);

        Assert.Multiple(() =>
        {
            Assert.That(destination.SourceSize.Value, Is.EqualTo(source.SourceSize.Value));
            Assert.That(destination.DestinationSize.Value, Is.EqualTo(source.DestinationSize.Value));
            Assert.That(destination.FrameRate.Value, Is.EqualTo(source.FrameRate.Value));
            Assert.That(destination.Bitrate.Value, Is.EqualTo(source.Bitrate.Value));
            Assert.That(destination.KeyframeRate.Value, Is.EqualTo(source.KeyframeRate.Value));
        });
    }

    [Test]
    public void CopyTo_NullArguments_DoNotThrow()
    {
        var settings = new VideoEncoderSettings { Bitrate = { Value = 7_000_000 } };

        Assert.Multiple(() =>
        {
            Assert.That(() => EncoderSettingsJson.CopyTo(null, settings), Throws.Nothing);
            Assert.That(() => EncoderSettingsJson.CopyTo(settings, null), Throws.Nothing);
        });
        Assert.That(settings.Bitrate.Value, Is.EqualTo(7_000_000));
    }
}

using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Encoding;

namespace Beutl.UnitTests.Editor.Services;

public class EncoderSettingsJsonTests
{
    [Test]
    public void Serialize_NullSettings_ReturnsNull()
    {
        Assert.That(EncoderSettingsJson.Serialize(null), Is.Null);
    }

    [Test]
    public void Populate_PopulatesVideoSettings()
    {
        var source = new VideoEncoderSettings
        {
            SourceSize = new PixelSize(1920, 1080),
            DestinationSize = new PixelSize(1280, 720),
            FrameRate = new Rational(60, 1),
            Bitrate = 7_000_000,
            KeyframeRate = 24
        };
        var target = new VideoEncoderSettings();

        EncoderSettingsJson.Populate(target, EncoderSettingsJson.Serialize(source)!);

        Assert.Multiple(() =>
        {
            Assert.That(target.SourceSize, Is.EqualTo(source.SourceSize));
            Assert.That(target.DestinationSize, Is.EqualTo(source.DestinationSize));
            Assert.That(target.FrameRate, Is.EqualTo(source.FrameRate));
            Assert.That(target.Bitrate, Is.EqualTo(source.Bitrate));
            Assert.That(target.KeyframeRate, Is.EqualTo(source.KeyframeRate));
        });
    }

    [Test]
    public void PopulateVideoPreset_PreservesTimelineOwnedSettings()
    {
        var preset = new VideoEncoderSettings
        {
            SourceSize = new PixelSize(640, 360),
            DestinationSize = new PixelSize(640, 360),
            FrameRate = new Rational(24, 1),
            Bitrate = 2_000_000,
            KeyframeRate = 8
        };
        var target = new VideoEncoderSettings
        {
            SourceSize = new PixelSize(3840, 2160),
            DestinationSize = new PixelSize(1920, 1080),
            FrameRate = new Rational(60, 1),
            Bitrate = 5_000_000,
            KeyframeRate = 12
        };

        EncoderSettingsJson.PopulateVideoPreset(target, EncoderSettingsJson.Serialize(preset)!);

        Assert.Multiple(() =>
        {
            Assert.That(target.SourceSize, Is.EqualTo(new PixelSize(3840, 2160)));
            Assert.That(target.DestinationSize, Is.EqualTo(new PixelSize(1920, 1080)));
            Assert.That(target.FrameRate, Is.EqualTo(new Rational(60, 1)));
            Assert.That(target.Bitrate, Is.EqualTo(preset.Bitrate));
            Assert.That(target.KeyframeRate, Is.EqualTo(preset.KeyframeRate));
        });
    }

    [Test]
    public void PopulateVideoPreset_WhenPopulateThrows_PreservesTimelineOwnedSettings()
    {
        var preset = new VideoEncoderSettings
        {
            SourceSize = new PixelSize(640, 360),
            DestinationSize = new PixelSize(640, 360),
            FrameRate = new Rational(24, 1),
            Bitrate = 2_000_000
        };
        JsonObject json = EncoderSettingsJson.Serialize(preset)!;
        json[nameof(VideoEncoderSettings.Bitrate)] = JsonValue.Create("invalid");
        var target = new VideoEncoderSettings
        {
            SourceSize = new PixelSize(3840, 2160),
            DestinationSize = new PixelSize(1920, 1080),
            FrameRate = new Rational(60, 1),
            Bitrate = 5_000_000
        };

        Assert.That(() => EncoderSettingsJson.PopulateVideoPreset(target, json), Throws.Exception);

        Assert.Multiple(() =>
        {
            Assert.That(target.SourceSize, Is.EqualTo(new PixelSize(3840, 2160)));
            Assert.That(target.DestinationSize, Is.EqualTo(new PixelSize(1920, 1080)));
            Assert.That(target.FrameRate, Is.EqualTo(new Rational(60, 1)));
        });
    }

    [Test]
    public void PopulateAudioPreset_PreservesSampleRate()
    {
        var preset = new AudioEncoderSettings
        {
            SampleRate = 48000,
            Channels = 1,
            Bitrate = 96_000
        };
        var target = new AudioEncoderSettings
        {
            SampleRate = 44100,
            Channels = 2,
            Bitrate = 128_000
        };

        EncoderSettingsJson.PopulateAudioPreset(target, EncoderSettingsJson.Serialize(preset)!);

        Assert.Multiple(() =>
        {
            Assert.That(target.SampleRate, Is.EqualTo(44100));
            Assert.That(target.Channels, Is.EqualTo(preset.Channels));
            Assert.That(target.Bitrate, Is.EqualTo(preset.Bitrate));
        });
    }

    [Test]
    public void PopulateAudioPreset_WhenPopulateThrows_PreservesSampleRate()
    {
        var preset = new AudioEncoderSettings
        {
            SampleRate = 48000,
            Channels = 1,
            Bitrate = 96_000
        };
        JsonObject json = EncoderSettingsJson.Serialize(preset)!;
        json[nameof(AudioEncoderSettings.Channels)] = JsonValue.Create("invalid");
        var target = new AudioEncoderSettings
        {
            SampleRate = 44100,
            Channels = 2,
            Bitrate = 128_000
        };

        Assert.That(() => EncoderSettingsJson.PopulateAudioPreset(target, json), Throws.Exception);

        Assert.That(target.SampleRate, Is.EqualTo(44100));
    }
}

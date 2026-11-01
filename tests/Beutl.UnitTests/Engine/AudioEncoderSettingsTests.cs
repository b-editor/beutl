using Beutl.Media.Encoding;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class AudioEncoderSettingsTests
{
    [Test]
    public void SetChannels_ScalesTheTotalBitrateOncePerChange()
    {
        var settings = new AudioEncoderSettings();

        foreach ((int channels, int bitrate) in new[] { (6, 384_000), (8, 512_000), (2, 128_000) })
        {
            settings.SetChannels(channels);
            Assert.That(settings.Channels.Value, Is.EqualTo(channels));
            Assert.That(settings.Bitrate.Value, Is.EqualTo(bitrate));
        }
    }

    [TestCase(-1)]
    [TestCase(0)]
    public void SetChannels_KeepsNonPositiveBitrate(int bitrate)
    {
        var settings = new AudioEncoderSettings { Bitrate = { Value = bitrate } };

        settings.SetChannels(6);

        Assert.That(settings.Channels.Value, Is.EqualTo(6));
        Assert.That(settings.Bitrate.Value, Is.EqualTo(bitrate));
    }

    [TestCase(128_001, 6, 8, 170_668)]
    [TestCase(128_001, 2, 1, 64_001)]
    [TestCase(128_000, 0, 6, 128_000)]
    public void SetChannels_RoundsTheRatioAndToleratesAnUnknownOldCount(int bitrate, int oldChannels, int channels, int expected)
    {
        var settings = new AudioEncoderSettings
        {
            Channels = { Value = oldChannels },
            Bitrate = { Value = bitrate }
        };

        settings.SetChannels(channels);

        Assert.That(settings.Channels.Value, Is.EqualTo(channels));
        Assert.That(settings.Bitrate.Value, Is.EqualTo(expected));
    }
}

using Beutl.Embedding.MediaFoundation.Decoding;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
public class MFDecodingSettingsTests
{
    [Test]
    public void Defaults_AreApplied()
    {
        var settings = new MFDecodingSettings();
        Assert.Multiple(() =>
        {
            Assert.That(settings.ThresholdFrameCount, Is.EqualTo(30));
            Assert.That(settings.MaxVideoBufferSize, Is.EqualTo(4));
        });
    }

    [Test]
    public void SampleCacheOptions_DefaultBufferSize_Is4()
        => Assert.That(new MFSampleCacheOptions().MaxVideoBufferSize, Is.EqualTo(4));

    // Guards the AffectsConfig re-registration: PackageManager only persists extension settings from
    // the ConfigurationChanged event, so without AffectsConfig these live settings would apply only
    // in memory and reset on reload. Dropping the AffectsConfig call must fail these tests.
    [Test]
    public void ChangingThresholdFrameCount_RaisesConfigurationChanged()
    {
        var settings = new MFDecodingSettings();
        int raised = 0;
        settings.ConfigurationChanged += (_, _) => raised++;

        settings.ThresholdFrameCount += 1;

        Assert.That(raised, Is.GreaterThan(0));
    }

    [Test]
    public void ChangingMaxVideoBufferSize_RaisesConfigurationChanged()
    {
        var settings = new MFDecodingSettings();
        int raised = 0;
        settings.ConfigurationChanged += (_, _) => raised++;

        settings.MaxVideoBufferSize += 1;

        Assert.That(raised, Is.GreaterThan(0));
    }
}

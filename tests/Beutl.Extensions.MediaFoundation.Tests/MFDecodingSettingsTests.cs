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

    // Guards the AffectsConfig re-registration: PackageManager only persists extension settings from
    // the ConfigurationChanged event, so without AffectsConfig these live settings would apply only
    // in memory and reset on reload. Dropping the AffectsConfig call must fail these tests.
    [TestCase(nameof(MFDecodingSettings.ThresholdFrameCount))]
    [TestCase(nameof(MFDecodingSettings.MaxVideoBufferSize))]
    public void ChangingASetting_RaisesConfigurationChanged(string setting)
    {
        var settings = new MFDecodingSettings();
        int raised = 0;
        settings.ConfigurationChanged += (_, _) => raised++;

        if (setting == nameof(MFDecodingSettings.ThresholdFrameCount))
            settings.ThresholdFrameCount += 1;
        else
            settings.MaxVideoBufferSize += 1;

        Assert.That(raised, Is.GreaterThan(0));
    }
}

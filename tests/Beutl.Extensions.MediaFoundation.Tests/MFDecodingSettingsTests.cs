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
}

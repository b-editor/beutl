using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public class ProxyPresetDefinitionsTests
{
    [Test]
    public void Get_ReturnsBuiltInParameters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProxyPresetDefinitions.Get(ProxyPreset.Half).Crf, Is.EqualTo(25));
            Assert.That(ProxyPresetDefinitions.Get(ProxyPreset.Quarter).Crf, Is.EqualTo(26));
            Assert.That(ProxyPresetDefinitions.Get(ProxyPreset.Eighth).Crf, Is.EqualTo(28));
        });
    }

    [Test]
    public void Get_UnknownPresetValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProxyPresetDefinitions.Get((ProxyPreset)99));
    }
}

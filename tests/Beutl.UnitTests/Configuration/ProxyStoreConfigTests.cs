using Beutl.Configuration;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class ProxyStoreConfigTests
{
    [Test]
    public void Defaults_AreAbsoluteAndBounded()
    {
        var config = new ProxyStoreConfig();

        Assert.Multiple(() =>
        {
            Assert.That(Path.IsPathFullyQualified(config.StoreRootPath), Is.True);
            Assert.That(config.MaxTotalBytes, Is.EqualTo(ProxyStoreConfig.DefaultTotalBytes));
            Assert.That(config.DefaultPreset, Is.EqualTo(2), "2 is ProxyPreset.Quarter without a Configuration -> Engine reference.");
        });
    }

    [Test]
    public void MaxTotalBytes_ClampsToSupportedRange()
    {
        var config = new ProxyStoreConfig
        {
            MaxTotalBytes = 1,
        };
        Assert.That(config.MaxTotalBytes, Is.EqualTo(ProxyStoreConfig.MinTotalBytes));

        config.MaxTotalBytes = long.MaxValue;
        Assert.That(config.MaxTotalBytes, Is.EqualTo(ProxyStoreConfig.MaxTotalBytesLimit));
    }

    [Test]
    public void StoreRootPath_ResolvesToAbsolutePath()
    {
        var config = new ProxyStoreConfig
        {
            StoreRootPath = "relative-proxies",
        };

        Assert.That(Path.IsPathFullyQualified(config.StoreRootPath), Is.True);
    }

    [Test]
    public void StoreRootPath_EmptyValueResetsToDefault()
    {
        var config = new ProxyStoreConfig
        {
            StoreRootPath = "custom-proxies",
        };

        config.StoreRootPath = string.Empty;

        Assert.That(config.StoreRootPath, Is.EqualTo(ProxyStoreConfig.DefaultStoreRootPath));
    }

    [Test]
    public void DefaultPreset_ClampsToKnownPresetRange()
    {
        var config = new ProxyStoreConfig
        {
            DefaultPreset = 0,
        };
        Assert.That(config.DefaultPreset, Is.EqualTo(ProxyStoreConfig.MinPreset));

        config.DefaultPreset = 99;
        Assert.That(config.DefaultPreset, Is.EqualTo(ProxyStoreConfig.MaxPreset));

        config.DefaultPreset = (int)ProxyPreset.Half;
        Assert.That(config.DefaultPreset, Is.EqualTo((int)ProxyPreset.Half));
    }

    // ProxyStoreConfig.DefaultPreset is an int (Configuration cannot reference Engine, so it can't take a
    // dependency on ProxyPreset directly). This guards the magic-number coupling: if the enum value of
    // ProxyPreset.Quarter ever changes, this test fails and forces the int default to be updated in lockstep.
    [Test]
    public void DefaultPreset_MatchesProxyPresetQuarterEnum()
    {
        Assert.Multiple(() =>
        {
            Assert.That((int)ProxyPreset.Quarter, Is.EqualTo(2), "ProxyPreset.Quarter must stay at 2 to match the int default.");
            Assert.That(new ProxyStoreConfig().DefaultPreset, Is.EqualTo((int)ProxyPreset.Quarter));
        });
    }
}

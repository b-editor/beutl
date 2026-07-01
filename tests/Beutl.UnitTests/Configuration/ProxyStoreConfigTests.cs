using Beutl.Configuration;

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
}

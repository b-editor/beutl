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
            Assert.That(Path.IsPathFullyQualified(config.StoreRootPath.Value), Is.True);
            Assert.That(config.MaxTotalBytes.Value, Is.EqualTo(ProxyStoreConfig.DefaultTotalBytes));
        });
    }

    [Test]
    public void MaxTotalBytes_ClampsToSupportedRange()
    {
        var config = new ProxyStoreConfig
        {
            MaxTotalBytes = { Value = 1 },
        };
        Assert.That(config.MaxTotalBytes.Value, Is.EqualTo(ProxyStoreConfig.MinTotalBytes));

        config.MaxTotalBytes.Value = long.MaxValue;
        Assert.That(config.MaxTotalBytes.Value, Is.EqualTo(ProxyStoreConfig.MaxTotalBytesLimit));
    }

    [Test]
    public void StoreRootPath_ResolvesToAbsolutePath()
    {
        var config = new ProxyStoreConfig
        {
            StoreRootPath = { Value = "relative-proxies" },
        };

        Assert.That(Path.IsPathFullyQualified(config.StoreRootPath.Value), Is.True);
    }

    // A malformed persisted value must not throw on access — Path.GetFullPath rejects a null character —
    // so the accessor degrades to the default. Otherwise the startup fallback, which reads this accessor
    // during recovery, would rethrow and never reach the default store location.
    [Test]
    public void StoreRootPath_MalformedValue_DegradesToDefaultWithoutThrowing()
    {
        var config = new ProxyStoreConfig();
        string malformed = "bad\0path";

        Assert.DoesNotThrow(() => config.StoreRootPath.Value = malformed);
        Assert.That(config.StoreRootPath.Value, Is.EqualTo(Path.GetFullPath(ProxyStoreConfig.DefaultStoreRootPath)));
    }

    [Test]
    public void StoreRootPath_EmptyValueResetsToDefault()
    {
        var config = new ProxyStoreConfig
        {
            StoreRootPath = { Value = "custom-proxies" },
        };

        config.StoreRootPath.Value = string.Empty;

        Assert.That(config.StoreRootPath.Value, Is.EqualTo(ProxyStoreConfig.DefaultStoreRootPath));
    }

    [Test]
    public void DefaultPreset_ClampsToKnownPresetRange()
    {
        var config = new ProxyStoreConfig
        {
            DefaultPreset = { Value = 0 },
        };
        Assert.That(config.DefaultPreset.Value, Is.EqualTo(ProxyStoreConfig.MinPreset));

        config.DefaultPreset.Value = 99;
        Assert.That(config.DefaultPreset.Value, Is.EqualTo(ProxyStoreConfig.MaxPreset));

        config.DefaultPreset.Value = (int)ProxyPreset.Half;
        Assert.That(config.DefaultPreset.Value, Is.EqualTo((int)ProxyPreset.Half));
    }

    [Test]
    public void DefaultPreset_MatchesProxyPresetQuarterEnum()
    {
        Assert.Multiple(() =>
        {
            Assert.That((int)ProxyPreset.Quarter, Is.EqualTo(2), "ProxyPreset.Quarter must stay at 2 to match the int default.");
            Assert.That(new ProxyStoreConfig().DefaultPreset.Value, Is.EqualTo((int)ProxyPreset.Quarter));
        });
    }

    // The settings page binds the proxy max size as a free-form TextBox (double), so gib
    // can be arbitrarily large, NaN, or infinite; the conversion must not throw.
    [TestCase(50d, ExpectedResult = 50L * 1024 * 1024 * 1024)]
    [TestCase(5d, ExpectedResult = ProxyStoreConfig.MinTotalBytes)]
    [TestCase(500d, ExpectedResult = ProxyStoreConfig.MaxTotalBytesLimit)]
    [TestCase(4d, ExpectedResult = ProxyStoreConfig.MinTotalBytes)]
    [TestCase(0d, ExpectedResult = ProxyStoreConfig.MinTotalBytes)]
    [TestCase(-1d, ExpectedResult = ProxyStoreConfig.MinTotalBytes)]
    [TestCase(1e10, ExpectedResult = ProxyStoreConfig.MaxTotalBytesLimit)]
    [TestCase(double.MaxValue, ExpectedResult = ProxyStoreConfig.MaxTotalBytesLimit)]
    [TestCase(double.PositiveInfinity, ExpectedResult = ProxyStoreConfig.MaxTotalBytesLimit)]
    [TestCase(double.NegativeInfinity, ExpectedResult = ProxyStoreConfig.MinTotalBytes)]
    public long ClampTotalBytesFromGiB_ClampsFreeFormInputWithoutThrowing(double gib)
    {
        return ProxyStoreConfig.ClampTotalBytesFromGiB(gib);
    }

    [Test]
    public void ClampTotalBytesFromGiB_NaNInput_ReturnsDefaultWithoutThrowing()
    {
        Assert.That(ProxyStoreConfig.ClampTotalBytesFromGiB(double.NaN), Is.EqualTo(ProxyStoreConfig.DefaultTotalBytes));
    }
}

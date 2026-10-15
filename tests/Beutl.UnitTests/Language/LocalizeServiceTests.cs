using System.Globalization;
using Beutl.Language;

namespace Beutl.UnitTests.Language;

[TestFixture]
public class LocalizeServiceTests
{
    [Test]
    public void SupportedCultures_returns_all_supported_cultures()
    {
        string[] names = LocalizeService.Instance.SupportedCultures().Select(x => x.Name).ToArray();

        Assert.That(names, Is.EquivalentTo(new[] { "en-US", "ja-JP", "zh-CN", "ko-KR", "es" }));
    }

    [TestCase("es-MX", "es")]
    [TestCase("es-ES", "es")]
    [TestCase("es", "es")]
    [TestCase("en-US", "en-US")]
    [TestCase("ja-JP", "ja-JP")]
    [TestCase("ko-KR", "ko-KR")]
    [TestCase("zh-CN", "zh-CN")]
    public void ResolveSupportedCulture_maps_a_culture_onto_the_entry_the_picker_holds(string name, string expected)
    {
        CultureInfo? resolved = LocalizeService.Instance.ResolveSupportedCulture(CultureInfo.GetCultureInfo(name));

        Assert.That(resolved?.Name, Is.EqualTo(expected));
        Assert.That(LocalizeService.Instance.SupportedCultures(), Does.Contain(resolved));
    }

    [TestCase("fr-FR")]
    [TestCase("de-DE")]
    [TestCase("en-GB")]
    [TestCase("")] // The invariant culture.
    public void ResolveSupportedCulture_returns_null_for_an_unsupported_culture(string name)
    {
        Assert.That(LocalizeService.Instance.ResolveSupportedCulture(CultureInfo.GetCultureInfo(name)), Is.Null);
    }
}

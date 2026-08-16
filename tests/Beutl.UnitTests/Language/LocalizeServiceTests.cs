using System.Globalization;
using Beutl.Language;

namespace Beutl.UnitTests.Language;

[TestFixture]
public class LocalizeServiceTests
{
    [TestCase("en-US", true)]
    [TestCase("ja-JP", true)]
    [TestCase("zh-CN", true)]
    [TestCase("ko-KR", true)]
    [TestCase("es", true)]
    [TestCase("es-ES", true)]
    [TestCase("es-MX", true)]
    [TestCase("fr-FR", false)]
    [TestCase("de-DE", false)]
    [TestCase("en-GB", false)]
    public void IsSupportedCulture_matches_exact_and_parent_cultures(string name, bool expected)
    {
        var ci = CultureInfo.GetCultureInfo(name);

        Assert.That(LocalizeService.Instance.IsSupportedCulture(ci), Is.EqualTo(expected));
    }

    [Test]
    public void IsSupportedCulture_rejects_invariant_culture()
    {
        Assert.That(LocalizeService.Instance.IsSupportedCulture(CultureInfo.InvariantCulture), Is.False);
    }

    [Test]
    public void SupportedCultures_returns_all_supported_cultures()
    {
        string[] names = LocalizeService.Instance.SupportedCultures().Select(x => x.Name).ToArray();

        Assert.That(names, Is.EquivalentTo(new[] { "en-US", "ja-JP", "zh-CN", "ko-KR", "es" }));
    }

    [TestCase("es-MX", "es")]
    [TestCase("es-ES", "es")]
    [TestCase("es", "es")]
    [TestCase("ja-JP", "ja-JP")]
    [TestCase("zh-CN", "zh-CN")]
    public void ResolveSupportedCulture_maps_a_culture_onto_the_entry_the_picker_holds(string name, string expected)
    {
        CultureInfo? resolved = LocalizeService.Instance.ResolveSupportedCulture(CultureInfo.GetCultureInfo(name));

        Assert.That(resolved?.Name, Is.EqualTo(expected));
        Assert.That(LocalizeService.Instance.SupportedCultures(), Does.Contain(resolved));
    }

    [TestCase("fr-FR")]
    [TestCase("en-GB")]
    public void ResolveSupportedCulture_returns_null_for_an_unsupported_culture(string name)
    {
        Assert.That(LocalizeService.Instance.ResolveSupportedCulture(CultureInfo.GetCultureInfo(name)), Is.Null);
    }
}

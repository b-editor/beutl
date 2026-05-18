using System.Globalization;
using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

public class TokenizerHelperTests
{
    // 名前付きカルチャを user override 無しで取得する。
    // globalization-invariant モードのランタイムでは null を返し、テストはスキップさせる。
    private static CultureInfo? TryGetCulture(string name)
    {
        try
        {
            return new CultureInfo(name, useUserOverride: false);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    [Test]
    public void GetSeparator_NullProvider_ReturnsComma()
    {
        char c = TokenizerHelper.GetSeparatorFromFormatProvider(null);
        Assert.That(c, Is.EqualTo(','));
    }

    [Test]
    public void GetSeparator_InvariantCulture_ReturnsComma()
    {
        char c = TokenizerHelper.GetSeparatorFromFormatProvider(CultureInfo.InvariantCulture);
        Assert.That(c, Is.EqualTo(','));
    }

    [Test]
    public void GetSeparator_USEnglishCulture_ReturnsComma()
    {
        CultureInfo? en = TryGetCulture("en-US");
        Assume.That(
            en,
            Is.Not.Null,
            "en-US culture not available (globalization-invariant runtime)"
        );
        char c = TokenizerHelper.GetSeparatorFromFormatProvider(en);
        Assert.That(c, Is.EqualTo(','));
    }

    [Test]
    public void GetSeparator_GermanCulture_ReturnsSemicolon()
    {
        // ドイツ語ロケールでは小数点の区切り文字がカンマなので、
        // セパレーターはセミコロンへ切り替わる
        CultureInfo? de = TryGetCulture("de-DE");
        Assume.That(
            de,
            Is.Not.Null,
            "de-DE culture not available (globalization-invariant runtime)"
        );
        char c = TokenizerHelper.GetSeparatorFromFormatProvider(de);
        Assert.That(c, Is.EqualTo(';'));
    }

    [Test]
    public void GetSeparator_FrenchCulture_ReturnsSemicolon()
    {
        CultureInfo? fr = TryGetCulture("fr-FR");
        Assume.That(
            fr,
            Is.Not.Null,
            "fr-FR culture not available (globalization-invariant runtime)"
        );
        char c = TokenizerHelper.GetSeparatorFromFormatProvider(fr);
        Assert.That(c, Is.EqualTo(';'));
    }

    [Test]
    public void GetSeparator_NumberFormatInfoDirect_ReturnsComma()
    {
        var nfi = NumberFormatInfo.InvariantInfo;
        char c = TokenizerHelper.GetSeparatorFromFormatProvider(nfi);
        Assert.That(c, Is.EqualTo(','));
    }

    [Test]
    public void DefaultSeparatorChar_IsComma()
    {
        Assert.That(TokenizerHelper.DefaultSeparatorChar, Is.EqualTo(','));
    }
}

using System.Globalization;
using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

public class TokenizerHelperTests
{
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
        var en = CultureInfo.GetCultureInfo("en-US");
        char c = TokenizerHelper.GetSeparatorFromFormatProvider(en);
        Assert.That(c, Is.EqualTo(','));
    }

    [Test]
    public void GetSeparator_GermanCulture_ReturnsSemicolon()
    {
        // ドイツ語ロケールでは小数点の区切り文字がカンマなので、
        // セパレーターはセミコロンへ切り替わる
        var de = CultureInfo.GetCultureInfo("de-DE");
        char c = TokenizerHelper.GetSeparatorFromFormatProvider(de);
        Assert.That(c, Is.EqualTo(';'));
    }

    [Test]
    public void GetSeparator_FrenchCulture_ReturnsSemicolon()
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
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

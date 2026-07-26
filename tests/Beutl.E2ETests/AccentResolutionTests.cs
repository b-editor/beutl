using Avalonia.Controls;
using Avalonia.Media;
using Beutl.Controls.Styling;

namespace Beutl.E2ETests;

[TestFixture]
public class AccentResolutionTests
{
    // The picker no longer offers alpha, but settings written before it stopped still carry one, so
    // every applied accent goes through Normalize rather than only new picks.
    [TestCase(0x00, TestName = "Normalize_FullyTransparentAccent_BecomesOpaque")]
    [TestCase(0x80, TestName = "Normalize_HalfTransparentAccent_BecomesOpaque")]
    [TestCase(0xFF, TestName = "Normalize_OpaqueAccent_IsUnchanged")]
    public void Normalize_KeepsTheHue_AndDropsTheAlpha(byte alpha)
    {
        Color? normalized = AccentResolution.Normalize(Color.FromArgb(alpha, 0x25, 0x63, 0xEB));

        Assert.That(normalized, Is.EqualTo(Color.FromRgb(0x25, 0x63, 0xEB)));
    }

    // Null is "no accent Beutl resolves" — the OS accent — and must stay distinguishable from one.
    [Test]
    public void Normalize_PassesNullThrough()
    {
        Assert.That(AccentResolution.Normalize(null), Is.Null);
    }

    // A transparent accent reads as light on its RGB channels alone, so an unnormalized value would
    // pick black and paint it over whatever shows through — on this theme, a near-black surface.
    [Test]
    public void Normalize_MakesATransparentWhiteAccentResolveAsALightOne()
    {
        var transparentWhite = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);

        Color? normalized = AccentResolution.Normalize(transparentWhite);

        Assert.Multiple(() =>
        {
            Assert.That(normalized, Is.EqualTo(Colors.White), "the accent Beutl paints is opaque");
            Assert.That(AccentResolution.ResolveForegroundOn(normalized!.Value), Is.EqualTo(Colors.Black),
                "black is right against the opaque white it now actually paints");
        });
    }

    // The accent picker offers the whole named-color palette, so either extreme is one click away.
    [TestCase(0x25, 0x63, 0xEB, TestName = "ResolveForegroundOn_TheDesignBlue_IsWhite")]
    [TestCase(0x00, 0x00, 0x00, TestName = "ResolveForegroundOn_Black_IsWhite")]
    public void ResolveForegroundOn_DarkAccent_IsWhite(byte r, byte g, byte b)
    {
        Assert.That(AccentResolution.ResolveForegroundOn(Color.FromRgb(r, g, b)), Is.EqualTo(Colors.White));
    }

    [TestCase(0xFF, 0xB9, 0x00, TestName = "ResolveForegroundOn_Amber_IsBlack")]
    [TestCase(0xFF, 0xFF, 0xFF, TestName = "ResolveForegroundOn_White_IsBlack")]
    public void ResolveForegroundOn_LightAccent_IsBlack(byte r, byte g, byte b)
    {
        Assert.That(AccentResolution.ResolveForegroundOn(Color.FromRgb(r, g, b)), Is.EqualTo(Colors.Black));
    }

    // The tokens share one foreground and differ only in alpha, so a control's label and its pressed
    // variant cannot end up on opposite sides of the accent.
    [Test]
    public void Apply_WritesTheOnAccentTokens_WithOneForegroundAndDistinctAlphas()
    {
        var resources = new ResourceDictionary();

        AccentResolution.ApplyTextOnAccent(resources, Color.FromRgb(0xFF, 0xB9, 0x00));

        Assert.Multiple(() =>
        {
            Assert.That(Token(resources, "TextOnAccentFillColorPrimary"), Is.EqualTo(Color.FromArgb(0xFF, 0, 0, 0)));
            Assert.That(Token(resources, "TextOnAccentFillColorSelectedText"), Is.EqualTo(Color.FromArgb(0xFF, 0, 0, 0)));
            Assert.That(Token(resources, "TextOnAccentFillColorSecondary"), Is.EqualTo(Color.FromArgb(0xC5, 0, 0, 0)));
        });
    }

    // Despite the name, the disabled token is never drawn on the accent: its consumers pair it with
    // AccentFillColorDisabled, a fixed translucent white. Deriving it would put this light accent's
    // black glyph on a near-black disabled fill.
    [Test]
    public void Apply_LeavesTheDisabledToken_ToTheTheme()
    {
        var resources = new ResourceDictionary();

        AccentResolution.ApplyTextOnAccent(resources, Color.FromRgb(0xFF, 0xB9, 0x00));

        Assert.That(resources.ContainsKey("TextOnAccentFillColorDisabled"), Is.False);
    }

    // Removal, not a white/black default: once Beutl stops resolving the accent the theme's own tokens
    // are the only defined answer again.
    [Test]
    public void Apply_WithNoAccent_RemovesTheTokensItWrote()
    {
        var resources = new ResourceDictionary();
        AccentResolution.ApplyTextOnAccent(resources, Color.FromRgb(0x25, 0x63, 0xEB));
        Assert.That(resources.ContainsKey("TextOnAccentFillColorPrimary"), Is.True, "precondition: the tokens were written");

        AccentResolution.ApplyTextOnAccent(resources, null);

        Assert.That(resources, Is.Empty);
    }

    private static Color Token(IResourceDictionary resources, string key)
    {
        Assert.That(resources.TryGetResource(key, null, out object? value), Is.True, $"'{key}' should be defined");
        Assert.That(value, Is.InstanceOf<Color>(), $"'{key}' should be a color, not a brush");
        return (Color)value!;
    }
}

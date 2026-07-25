using Avalonia.Controls;
using Avalonia.Media;
using Beutl.Controls.Styling;

namespace Beutl.E2ETests;

// Both shells derive their text-on-accent tokens from here, so this fixture is the single place the
// contrast rule and the removal-on-null behavior are pinned.
[TestFixture]
public class AccentTextResourcesTests
{
    // The accent picker offers the whole named-color palette, so either extreme is one click away.
    [TestCase(0x25, 0x63, 0xEB, TestName = "ResolveForegroundOn_TheDesignBlue_IsWhite")]
    [TestCase(0x00, 0x00, 0x00, TestName = "ResolveForegroundOn_Black_IsWhite")]
    public void ResolveForegroundOn_DarkAccent_IsWhite(byte r, byte g, byte b)
    {
        Assert.That(AccentTextResources.ResolveForegroundOn(Color.FromRgb(r, g, b)), Is.EqualTo(Colors.White));
    }

    [TestCase(0xFF, 0xB9, 0x00, TestName = "ResolveForegroundOn_Amber_IsBlack")]
    [TestCase(0xFF, 0xFF, 0xFF, TestName = "ResolveForegroundOn_White_IsBlack")]
    public void ResolveForegroundOn_LightAccent_IsBlack(byte r, byte g, byte b)
    {
        Assert.That(AccentTextResources.ResolveForegroundOn(Color.FromRgb(r, g, b)), Is.EqualTo(Colors.Black));
    }

    // The four tokens share one foreground and differ only in alpha, so a control's primary label and
    // its disabled variant cannot end up on opposite sides of the accent.
    [Test]
    public void Apply_WritesAllFourTokens_WithOneForegroundAndDistinctAlphas()
    {
        var resources = new ResourceDictionary();

        AccentTextResources.Apply(resources, Color.FromRgb(0xFF, 0xB9, 0x00));

        Assert.Multiple(() =>
        {
            Assert.That(Token(resources, "TextOnAccentFillColorPrimary"), Is.EqualTo(Color.FromArgb(0xFF, 0, 0, 0)));
            Assert.That(Token(resources, "TextOnAccentFillColorSelectedText"), Is.EqualTo(Color.FromArgb(0xFF, 0, 0, 0)));
            Assert.That(Token(resources, "TextOnAccentFillColorSecondary"), Is.EqualTo(Color.FromArgb(0xC5, 0, 0, 0)));
            Assert.That(Token(resources, "TextOnAccentFillColorDisabled"), Is.EqualTo(Color.FromArgb(0x87, 0, 0, 0)));
        });
    }

    // Removal, not a white/black default: once Beutl stops resolving the accent the theme's own tokens
    // are the only defined answer again.
    [Test]
    public void Apply_WithNoAccent_RemovesTheTokensItWrote()
    {
        var resources = new ResourceDictionary();
        AccentTextResources.Apply(resources, Color.FromRgb(0x25, 0x63, 0xEB));
        Assert.That(resources.ContainsKey("TextOnAccentFillColorPrimary"), Is.True, "precondition: the tokens were written");

        AccentTextResources.Apply(resources, null);

        Assert.That(resources, Is.Empty);
    }

    private static Color Token(IResourceDictionary resources, string key)
    {
        Assert.That(resources.TryGetResource(key, null, out object? value), Is.True, $"'{key}' should be defined");
        Assert.That(value, Is.InstanceOf<Color>(), $"'{key}' should be a color, not a brush");
        return (Color)value!;
    }
}

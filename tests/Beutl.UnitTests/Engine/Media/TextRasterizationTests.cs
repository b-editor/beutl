using System.Runtime.InteropServices;

using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media;

[TestFixture]
public class TextRasterizationTests
{
    [OneTimeSetUp]
    public void RegisterFonts()
    {
        _ = TypefaceProvider.Typeface();
    }

    // DirectWrite's grayscale glyph masks have about 16 coverage levels, so text that scales flickered on
    // Windows; CoreText and FreeType give well over a hundred (b-editor/beutl#2693).
    [Test]
    public void Text_IsAntialiasedWithFineCoverage()
    {
        using var text = new FormattedText { Font = new FontFamily("Roboto"), Size = 28 };
        using SKFont font = text.ToSKFont();
        using var surface = SKSurface.Create(new SKImageInfo(640, 48, SKColorType.Alpha8, SKAlphaType.Premul));
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        surface.Canvas.DrawText("Sphinx of black quartz, judge my vow", 4, 34, font, paint);

        using SKPixmap pixels = surface.PeekPixels();
        int levels = pixels.GetPixelSpan().ToArray().Where(alpha => alpha is > 0 and < 255).Distinct().Count();
        Assert.That(levels, Is.GreaterThan(64));
    }

    // The accessor only runs on Windows, where unit tests do not run in CI, and a SkiaSharp update can
    // rename the internal method behind it.
    [Test]
    public void FreeTypeFonts_WrapsANativeFontManager()
    {
        nint handle = CreateDefaultFontManager();
        using SKFontManager manager = FreeTypeFonts.Wrap(handle);

        Assert.That(manager.Handle, Is.EqualTo(handle));
    }

    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sk_fontmgr_create_default")]
    private static extern nint CreateDefaultFontManager();
}

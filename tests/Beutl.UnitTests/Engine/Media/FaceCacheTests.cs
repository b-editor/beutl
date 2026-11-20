using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media;

// On Windows the cache gets FreeType's manager; any manager exercises the same mapping, so CI on Linux covers it.
[TestFixture]
public class FaceCacheTests
{
    private const string Text = "Beutl AA";
    private SKTypeface _typeface = null!;

    [OneTimeSetUp]
    public void LoadFont()
    {
        using Stream stream = typeof(FaceCacheTests).Assembly
            .GetManifestResourceStream("Beutl.UnitTests.Assets.Font.Roboto-Regular.ttf")!;
        _typeface = SKTypeface.FromStream(stream);
    }

    [OneTimeTearDown]
    public void DisposeFont()
    {
        _typeface.Dispose();
    }

    [Test]
    public void Get_WithoutAManager_ReturnsTheTypefaceItself()
    {
        var cache = new FaceCache(null);

        Assert.That(cache.GetRenderFace(_typeface), Is.SameAs(_typeface));
    }

    [Test]
    public void Get_CreatesOneFaceFromTheSameFontData()
    {
        var cache = new FaceCache(SKFontManager.Default);

        SKTypeface face = cache.GetRenderFace(_typeface);

        using var expected = new SKFont(_typeface, 20);
        using var actual = new SKFont(face, 20);
        Assert.Multiple(() =>
        {
            Assert.That(face, Is.Not.SameAs(_typeface));
            Assert.That(cache.GetRenderFace(_typeface), Is.SameAs(face));
            Assert.That(face.FamilyName, Is.EqualTo(_typeface.FamilyName));
            Assert.That(actual.GetGlyphs(Text), Is.EqualTo(expected.GetGlyphs(Text)));
            Assert.That(actual.MeasureText(Text), Is.EqualTo(expected.MeasureText(Text)));
        });
    }
}

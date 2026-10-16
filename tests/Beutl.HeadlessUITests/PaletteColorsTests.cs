namespace Beutl.HeadlessUITests;

[TestFixture]
public class PaletteColorsTests
{
    [Test]
    public void The_palette_shows_each_color_once()
    {
        Assert.That(AppHelpers.GetPaletteColors(), Is.Unique);
    }
}

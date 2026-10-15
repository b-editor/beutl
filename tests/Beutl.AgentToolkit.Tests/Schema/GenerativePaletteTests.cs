using Beutl.AgentToolkit.Schema;
using Beutl.Media;

namespace Beutl.AgentToolkit.Tests.Schema;

[TestFixture]
public class GenerativePaletteTests
{
    [Test]
    public void GeneratePalette_IsDeterministicForSameSeedAndOffset()
    {
        var first = CompositionTemplateCatalog.GeneratePalette("determinism-seed", 2);
        var second = CompositionTemplateCatalog.GeneratePalette("determinism-seed", 2);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void GeneratePalette_ProducesManyDistinctPalettesAcrossSeeds()
    {
        var palettes = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 50; i++)
        {
            var palette = CompositionTemplateCatalog.GeneratePalette($"variety-seed-{i}", 0);
            palettes.Add($"{palette.BackgroundA}|{palette.BackgroundB}|{palette.Accent}|{palette.SecondaryAccent}|{palette.Foreground}");
        }

        Assert.That(palettes.Count, Is.GreaterThan(20), $"Only {palettes.Count} distinct palettes across 50 seeds.");
    }

    // Hues from 160 to 230 were once pushed out of the backgrounds and the foreground to suit a quality check that
    // no longer exists. The foreground is bright enough to keep its hue through the hex color.
    [Test]
    public void GeneratePalette_ForegroundCanBeTealTinted()
    {
        bool anyTeal = Enumerable.Range(0, 200)
            .Select(i => Color.Parse(CompositionTemplateCatalog.GeneratePalette($"teal-seed-{i}", 0).Foreground).ToHsv().H)
            .Any(hue => hue is > 170 and < 220);

        Assert.That(anyTeal, Is.True);
    }
}

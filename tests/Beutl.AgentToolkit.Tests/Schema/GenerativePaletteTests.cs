using Beutl.AgentToolkit.Schema;

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

        TestContext.Out.WriteLine($"Distinct palettes: {palettes.Count}/50");
        Assert.That(palettes.Count, Is.GreaterThan(20), $"Only {palettes.Count} distinct palettes across 50 seeds.");
    }

}

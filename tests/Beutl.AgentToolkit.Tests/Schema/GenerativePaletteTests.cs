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

        TestContext.Out.WriteLine($"Distinct palettes: {palettes.Count}/50");
        Assert.That(palettes.Count, Is.GreaterThan(20), $"Only {palettes.Count} distinct palettes across 50 seeds.");
    }

    [Test]
    public void GeneratePalette_OutputSpaceIsGateCleanForEveryGeneratedPalette()
    {
        int trips = 0;
        for (int seed = 0; seed < 120; seed++)
        {
            for (int offset = 0; offset < 6; offset++)
            {
                var palette = CompositionTemplateCatalog.GeneratePalette($"gate-seed-{seed}", offset);
                Color[] colors =
                [
                    ParseHex(palette.BackgroundA),
                    ParseHex(palette.BackgroundB),
                    ParseHex(palette.Accent),
                    ParseHex(palette.SecondaryAccent),
                    ParseHex(palette.Foreground),
                ];

                if (TripsPaletteHarmony(colors))
                {
                    trips++;
                }
            }
        }

        Assert.That(trips, Is.Zero, $"{trips} generated palettes tripped a paletteHarmony rule.");
    }

    // Mirrors the paletteHarmony advisory thresholds in QualityAnalyzer.AnalyzePalette so the test
    // fails if the generator's output space ever drifts into a warned region.
    private static bool TripsPaletteHarmony(Color[] colors)
    {
        double averageSaturation = colors.Average(c => c.ToHsv().S);
        double maxSaturation = colors.Max(c => c.ToHsv().S);
        double lumaRange = colors.Max(RelativeLuma) - colors.Min(RelativeLuma);

        bool hasDarkTeal = colors.Any(c => RelativeLuma(c) < 0.16 && HueIn(c, 160, 230));
        bool hasCyan = colors.Any(c => c.ToHsv() is { S: >= 55, V: >= 55 } && HueIn(c, 175, 210));
        bool hasMagenta = colors.Any(c => c.ToHsv() is { S: >= 55, V: >= 50 } && HueIn(c, 285, 335));

        bool darkTealCyanMagenta = hasDarkTeal && hasCyan && hasMagenta;
        bool oversaturated = colors.Length >= 3 && averageSaturation >= 68 && maxSaturation >= 88;
        bool lowContrast = colors.Length >= 2 && lumaRange < 0.18;

        return darkTealCyanMagenta || oversaturated || lowContrast;
    }

    private static bool HueIn(Color color, double start, double end)
    {
        double hue = color.ToHsv().H;
        return start <= end
            ? hue >= start && hue <= end
            : hue >= start || hue <= end;
    }

    private static double RelativeLuma(Color color)
        => ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;

    private static Color ParseHex(string hex)
    {
        string value = hex.TrimStart('#');
        byte a = Convert.ToByte(value.Substring(0, 2), 16);
        byte r = Convert.ToByte(value.Substring(2, 2), 16);
        byte g = Convert.ToByte(value.Substring(4, 2), 16);
        byte b = Convert.ToByte(value.Substring(6, 2), 16);
        return Color.FromArgb(a, r, g, b);
    }
}

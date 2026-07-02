using Beutl.AgentToolkit.Design;

namespace Beutl.AgentToolkit.Tests.Design;

public sealed class ColorHarmonyEngineTests
{
    [TestCase("analogous", new[] { 0.0, -30.0, 30.0 })]
    [TestCase("complementary", new[] { 0.0, 180.0 })]
    [TestCase("split-complementary", new[] { 0.0, 150.0, -150.0 })]
    [TestCase("triadic", new[] { 0.0, 120.0, -120.0 })]
    [TestCase("tetradic", new[] { 0.0, 90.0, 180.0, -90.0 })]
    [TestCase("monochromatic", new[] { 0.0 })]
    public void Harmony_schemes_produce_expected_hue_relationships(string scheme, double[] expectedOffsets)
    {
        DerivedPalette palette = ColorHarmonyEngine.Derive(42, "dark", scheme, 0.58);

        Assert.That(
            palette.HarmonyHues.Select(hue => hue.OffsetDegrees).ToArray(),
            Is.EqualTo(expectedOffsets));
        Assert.That(
            palette.HarmonyHues.Select(hue => hue.HueDegrees).ToArray(),
            Is.EqualTo(expectedOffsets.Select(offset => NormalizeHue(42 + offset)).ToArray()));
    }

    [Test]
    public void Derived_palette_guarantees_documented_contrast_floor_across_hue_and_tone_sweep()
    {
        string[] tones = ["dark", "light", "balanced"];
        string[] schemes = ["analogous", "complementary", "split-complementary", "triadic", "tetradic", "monochromatic"];

        foreach (string tone in tones)
        {
            foreach (string scheme in schemes)
            {
                for (int hue = 0; hue < 360; hue += 15)
                {
                    DerivedPalette palette = ColorHarmonyEngine.Derive(hue, tone, scheme, 0.72);
                    Dictionary<string, PaletteRoleColor> roles = palette.Roles.ToDictionary(role => role.Role);

                    Assert.Multiple(() =>
                    {
                        Assert.That(
                            ColorHarmonyEngine.ContrastRatio(roles["text-primary"].HexArgb, roles["bg-base"].HexArgb),
                            Is.GreaterThanOrEqualTo(ColorHarmonyEngine.TextContrastFloor),
                            $"{tone}/{scheme}/{hue}: text-primary vs bg-base");
                        Assert.That(
                            ColorHarmonyEngine.ContrastRatio(roles["text-primary"].HexArgb, roles["bg-accent"].HexArgb),
                            Is.GreaterThanOrEqualTo(ColorHarmonyEngine.TextContrastFloor),
                            $"{tone}/{scheme}/{hue}: text-primary vs bg-accent");
                        Assert.That(
                            ColorHarmonyEngine.ContrastRatio(roles["foreground"].HexArgb, roles["bg-base"].HexArgb),
                            Is.GreaterThanOrEqualTo(ColorHarmonyEngine.ObjectContrastFloor),
                            $"{tone}/{scheme}/{hue}: foreground vs bg-base");
                        Assert.That(
                            ColorHarmonyEngine.ContrastRatio(roles["accent"].HexArgb, roles["bg-base"].HexArgb),
                            Is.GreaterThanOrEqualTo(ColorHarmonyEngine.ObjectContrastFloor),
                            $"{tone}/{scheme}/{hue}: accent vs bg-base");
                        Assert.That(palette.ContrastChecks.All(check => check.Passes), Is.True);
                    });
                }
            }
        }
    }

    [Test]
    public void Evaluate_palette_scores_hue_wheel_relationships_and_balance()
    {
        PaletteHarmonyEvaluation harmonious = ColorHarmonyEngine.EvaluatePalette(
        [
            "#ff0c1626",
            "#ff1a7fb0",
            "#fff3f8ff",
            "#ff46c7d8"
        ]);
        PaletteHarmonyEvaluation clashing = ColorHarmonyEngine.EvaluatePalette(
        [
            "#ff101010",
            "#ffff2020",
            "#ffff8a00",
            "#ffffff00",
            "#ff00ff35",
            "#ff00ffff",
            "#ffff00ff"
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(harmonious.IsHarmonious, Is.True);
            Assert.That(harmonious.Score, Is.GreaterThanOrEqualTo(0.68));
            Assert.That(harmonious.BestScheme, Is.Not.Empty);

            Assert.That(clashing.IsHarmonious, Is.False);
            Assert.That(clashing.Score, Is.LessThan(harmonious.Score));
            Assert.That(clashing.SaturationBalanceScore, Is.LessThan(harmonious.SaturationBalanceScore));
        });
    }

    private static double NormalizeHue(double value)
    {
        double result = value % 360;
        return result < 0 ? result + 360 : result;
    }
}

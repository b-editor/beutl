using Beutl.Media;

namespace Beutl.AgentToolkit.Schema;

public sealed partial class CompositionTemplateCatalog
{
    private static Palette ResolvePalette(CompositionContext context, int offset)
    {
        Palette generated = GeneratePalette(context.Seed, offset);
        SetIfMissing(context, "backgroundA", generated.BackgroundA);
        SetIfMissing(context, "backgroundB", generated.BackgroundB);
        SetIfMissing(context, "accent", generated.Accent);
        SetIfMissing(context, "secondaryAccent", generated.SecondaryAccent);
        SetIfMissing(context, "foreground", generated.Foreground);

        return new Palette(
            ReadString(context.ResolvedProps, "backgroundA", generated.BackgroundA),
            ReadString(context.ResolvedProps, "backgroundB", generated.BackgroundB),
            ReadString(context.ResolvedProps, "accent", generated.Accent),
            ReadString(context.ResolvedProps, "secondaryAccent", generated.SecondaryAccent),
            ReadString(context.ResolvedProps, "foreground", generated.Foreground));
    }

    internal static Palette GeneratePalette(string seed, int offset)
    {
        const double tealBandStart = 160;
        const double tealBandEnd = 230;
        const double maxSaturation = 78; // stays below the oversaturation max-sat threshold (88)
        const double minAccentLuma = 0.22; // stays above the dark-teal luma threshold (0.16)

        var rng = new SeededValues($"{seed}:palette:{offset}");

        double baseHue = rng.Range(0f, 360f);

        // Harmonic rotation families keep accent/secondary in a pleasing relationship while giving
        // each seed a distinct scheme.
        double[] accentRotations = [150, 165, 180, 195, 210, 30, 330, 120, 240];
        double accentRotation = accentRotations[rng.NextInt(accentRotations.Length)];
        double[] secondaryRotations = [-40, -25, 25, 40, 150, 180];
        double secondaryRotation = secondaryRotations[rng.NextInt(secondaryRotations.Length)];

        double accentHue = Wrap360(baseHue + accentRotation);
        double secondaryHue = Wrap360(accentHue + secondaryRotation);
        double backgroundHue = AvoidHueBand(baseHue, tealBandStart, tealBandEnd);
        double backgroundHueB = AvoidHueBand(baseHue + rng.Range(-12f, 12f), tealBandStart, tealBandEnd);
        double foregroundHue = AvoidHueBand(baseHue + rng.Range(-8f, 8f), tealBandStart, tealBandEnd);

        // Backgrounds: subtle dark tints (low saturation, low value).
        double bgSatA = rng.Range(8f, 26f);
        double bgValA = rng.Range(8f, 14f);
        double bgSatB = rng.Range(14f, 32f);
        double bgValB = bgValA + rng.Range(6f, 12f);

        // Accent: the vivid focal color, capped below the oversaturation ceiling.
        double accentSat = rng.Range(52f, (float)maxSaturation);
        double accentVal = rng.Range(72f, 92f);

        // Secondary accent: a muted support color.
        double secondarySat = rng.Range(20f, 44f);
        double secondaryVal = rng.Range(60f, 84f);

        // Foreground: near-white with a faint tint for readability against the dark backgrounds.
        double fgSat = rng.Range(4f, 14f);
        double fgVal = rng.Range(92f, 99f);

        (accentSat, accentVal) = EnsureMinLumaInBand(accentHue, accentSat, accentVal, tealBandStart, tealBandEnd, minAccentLuma);
        (secondarySat, secondaryVal) = EnsureMinLumaInBand(secondaryHue, secondarySat, secondaryVal, tealBandStart, tealBandEnd, minAccentLuma);

        return new Palette(
            HsvHex(backgroundHue, bgSatA, bgValA),
            HsvHex(backgroundHueB, bgSatB, bgValB),
            HsvHex(accentHue, accentSat, accentVal),
            HsvHex(secondaryHue, secondarySat, secondaryVal),
            HsvHex(foregroundHue, fgSat, fgVal));
    }

    private static double Wrap360(double hue)
    {
        hue %= 360;
        return hue < 0 ? hue + 360 : hue;
    }

    // Rotates a hue out of [start, end] (the dark-teal band) toward the nearer edge so a dark tint
    // built from it can never satisfy the dark-teal predicate.
    private static double AvoidHueBand(double hue, double start, double end)
    {
        double wrapped = Wrap360(hue);
        if (wrapped < start || wrapped > end)
        {
            return wrapped;
        }

        double mid = (start + end) / 2;
        return wrapped < mid ? Wrap360(start - 5) : Wrap360(end + 5);
    }

    // When a non-background hue lands in the dark-teal band, raise value (and shed a little
    // saturation) until its relative luma clears the dark-teal threshold, so it is never a dark teal.
    private static (double Saturation, double Value) EnsureMinLumaInBand(
        double hue, double saturation, double value, double bandStart, double bandEnd, double minLuma)
    {
        if (hue < bandStart || hue > bandEnd)
        {
            return (saturation, value);
        }

        for (int i = 0; i < 24 && LumaOf(hue, saturation, value) < minLuma; i++)
        {
            value = Math.Min(100, value + 4);
            saturation = Math.Max(0, saturation - 2);
        }

        return (saturation, value);
    }

    private static double LumaOf(double hue, double saturation, double value)
    {
        Color color = new Hsv((float)Wrap360(hue), (float)Math.Clamp(saturation, 0, 100), (float)Math.Clamp(value, 0, 100), 1f).ToColor();
        return ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;
    }

    private static string HsvHex(double hue, double saturation, double value)
    {
        Color color = new Hsv((float)Wrap360(hue), (float)Math.Clamp(saturation, 0, 100), (float)Math.Clamp(value, 0, 100), 1f).ToColor();
        return $"#ff{color.R:x2}{color.G:x2}{color.B:x2}";
    }

    private static void SetIfMissing(CompositionContext context, string name, string value)
    {
        if (!context.InputProps.ContainsKey(name))
        {
            context.ResolvedProps[name] = value;
        }
    }

    internal sealed record Palette(
        string BackgroundA,
        string BackgroundB,
        string Accent,
        string SecondaryAccent,
        string Foreground);
}

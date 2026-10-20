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
        double backgroundHue = Wrap360(baseHue);
        double backgroundHueB = Wrap360(baseHue + rng.Range(-12f, 12f));
        double foregroundHue = Wrap360(baseHue + rng.Range(-8f, 8f));

        // Backgrounds: subtle dark tints (low saturation, low value).
        double bgSatA = rng.Range(8f, 26f);
        double bgValA = rng.Range(8f, 14f);
        double bgSatB = rng.Range(14f, 32f);
        double bgValB = bgValA + rng.Range(6f, 12f);

        // Accent: the vivid focal color.
        double accentSat = rng.Range(52f, 78f);
        double accentVal = rng.Range(72f, 92f);

        // Secondary accent: a muted support color.
        double secondarySat = rng.Range(20f, 44f);
        double secondaryVal = rng.Range(60f, 84f);

        // Foreground: near-white with a faint tint for readability against the dark backgrounds.
        double fgSat = rng.Range(4f, 14f);
        double fgVal = rng.Range(92f, 99f);

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

    private static string HsvHex(double hue, double saturation, double value)
    {
        Color color = HsvToColor(hue, saturation, value);
        return $"#ff{color.R:x2}{color.G:x2}{color.B:x2}";
    }

    private static Color HsvToColor(double hue, double saturation, double value)
    {
        return new Hsv((float)Wrap360(hue), (float)Math.Clamp(saturation, 0, 100), (float)Math.Clamp(value, 0, 100), 1f).ToColor();
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

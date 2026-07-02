using System.Globalization;
using System.Text.RegularExpressions;
using Beutl.AgentToolkit.Sessions;

namespace Beutl.AgentToolkit.Design;

public enum HarmonyScheme
{
    Analogous,
    Complementary,
    SplitComplementary,
    Triadic,
    Tetradic,
    Monochromatic
}

public enum TonalSeed
{
    Dark,
    Light,
    Balanced
}

public sealed record HarmonyHue(
    string Name,
    double HueDegrees,
    double OffsetDegrees);

public sealed record PaletteRoleColor(
    string Role,
    string HexArgb,
    double HueDegrees,
    double Saturation,
    double Lightness,
    string Usage);

public sealed record PaletteContrastCheck(
    string ForegroundRole,
    string BackgroundRole,
    double Ratio,
    double Floor,
    bool Passes);

public sealed record HueBand(
    int Index,
    string Name,
    double StartDegrees,
    double EndDegrees);

public sealed record DerivedPalette(
    string Scheme,
    double BaseHueDegrees,
    string TonalSeed,
    double Saturation,
    double TextContrastFloor,
    double ObjectContrastFloor,
    HueBand BaseHueBand,
    IReadOnlyList<HarmonyHue> HarmonyHues,
    IReadOnlyList<PaletteRoleColor> Roles,
    IReadOnlyList<PaletteContrastCheck> ContrastChecks);

public sealed record PaletteHarmonyEvaluation(
    int ColorCount,
    int ChromaticColorCount,
    string BestScheme,
    double Score,
    double HueRelationshipScore,
    double SaturationBalanceScore,
    double LumaBalanceScore,
    double AverageSaturation,
    double SaturationRange,
    double LumaRange,
    bool IsHarmonious,
    IReadOnlyList<string> Evidence);

public sealed record PaletteRepeatWarning(
    string Kind,
    string MatchedValue,
    IReadOnlyList<string> MatchedConceptLabels,
    string Message);

public static class ColorHarmonyEngine
{
    public const double TextContrastFloor = 4.5;
    public const double ObjectContrastFloor = 3.0;

    private static readonly string[] s_hueBandNames =
    [
        "red",
        "orange",
        "amber",
        "yellow-green",
        "green",
        "mint",
        "cyan",
        "azure",
        "blue",
        "violet",
        "purple",
        "magenta"
    ];

    private static readonly Dictionary<string, int> s_hueNameBands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = 0,
        ["crimson"] = 0,
        ["scarlet"] = 0,
        ["orange"] = 1,
        ["amber"] = 2,
        ["yellow"] = 2,
        ["gold"] = 2,
        ["lime"] = 3,
        ["yellow-green"] = 3,
        ["green"] = 4,
        ["mint"] = 5,
        ["teal"] = 5,
        ["cyan"] = 6,
        ["aqua"] = 6,
        ["azure"] = 7,
        ["blue"] = 8,
        ["indigo"] = 9,
        ["violet"] = 9,
        ["purple"] = 10,
        ["magenta"] = 11,
        ["pink"] = 11,
        ["rose"] = 11
    };

    public static DerivedPalette Derive(
        double baseHueDegrees,
        string? tonalSeed,
        string? harmonyScheme,
        double saturation)
    {
        HarmonyScheme scheme = ParseHarmonyScheme(harmonyScheme);
        TonalSeed tone = ParseTonalSeed(tonalSeed);
        double hue = NormalizeHue(baseHueDegrees);
        double sat = ClampFinite(saturation, 0.18, 0.72, 0.58);

        double[] offsets = GetHueOffsets(scheme);
        HarmonyHue[] harmonyHues = offsets
            .Select((offset, index) => new HarmonyHue(HarmonyName(index), NormalizeHue(hue + offset), NormalizeOffset(offset)))
            .ToArray();

        RoleTone roleTone = RoleTone.For(tone);
        double bgAccentHue = harmonyHues.Length > 1 ? harmonyHues[1].HueDegrees : NormalizeHue(hue + 12);
        double foregroundHue = harmonyHues.Length > 2 ? harmonyHues[2].HueDegrees : NormalizeHue(hue + 180);
        double accentHue = ResolveAccentHue(hue, scheme, harmonyHues);

        var bgBase = new HslColor(hue, sat * roleTone.BackgroundSaturationFactor, roleTone.BackgroundBaseLightness);
        var bgAccent = new HslColor(bgAccentHue, sat * roleTone.BackgroundAccentSaturationFactor, roleTone.BackgroundAccentLightness);
        var foreground = EnsureContrast(
            new HslColor(foregroundHue, sat * roleTone.ForegroundSaturationFactor, roleTone.ForegroundLightness),
            [bgBase],
            ObjectContrastFloor,
            roleTone.PreferLightObjects);
        var text = EnsureContrast(
            new HslColor(hue, Math.Min(0.20, sat * 0.30), roleTone.TextLightness),
            [bgBase, bgAccent],
            TextContrastFloor,
            roleTone.PreferLightObjects);
        var accent = EnsureContrast(
            new HslColor(accentHue, sat, roleTone.AccentLightness),
            [bgBase],
            ObjectContrastFloor,
            roleTone.PreferLightObjects);

        PaletteRoleColor[] roles =
        [
            ToRole("bg-base", bgBase, "Full-frame base surface. Use as the deepest background band."),
            ToRole("bg-accent", bgAccent, "Secondary surface or gradient stop. Keep it below text and foreground roles."),
            ToRole("foreground", foreground, "Primary non-text focal material or large readable marks."),
            ToRole("text-primary", text, "Primary readable text role. Guaranteed against bg-base and bg-accent."),
            ToRole("accent", accent, "Small emphasis, highlight, or motion accent. Use sparingly.")
        ];

        PaletteContrastCheck[] checks =
        [
            Contrast("text-primary", text, "bg-base", bgBase, TextContrastFloor),
            Contrast("text-primary", text, "bg-accent", bgAccent, TextContrastFloor),
            Contrast("foreground", foreground, "bg-base", bgBase, ObjectContrastFloor),
            Contrast("accent", accent, "bg-base", bgBase, ObjectContrastFloor)
        ];

        return new DerivedPalette(
            SchemeName(scheme),
            hue,
            ToneName(tone),
            sat,
            TextContrastFloor,
            ObjectContrastFloor,
            GetHueBand(hue),
            harmonyHues,
            roles,
            checks);
    }

    public static IReadOnlyList<PaletteRepeatWarning> FindRepeatWarnings(
        DerivedPalette palette,
        string? structuralSignature,
        IReadOnlyList<CreativeDirectionFingerprint> recent)
    {
        if (recent.Count == 0)
        {
            return [];
        }

        var warnings = new List<PaletteRepeatWarning>(2);
        string[] hueMatches = recent
            .Where(item => PaletteRolesContainHueBand(item.PaletteRoles, palette.BaseHueBand.Index))
            .Select(LabelFor)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (hueMatches.Length > 0)
        {
            warnings.Add(new PaletteRepeatWarning(
                "hueBand",
                $"{palette.BaseHueBand.Name} ({palette.BaseHueBand.StartDegrees:0}-{palette.BaseHueBand.EndDegrees:0} deg)",
                hueMatches,
                $"Derived base hue falls in the recent {palette.BaseHueBand.Name} band. Shift the base hue, tone, or accent balance unless the brief explicitly requires this family."));
        }

        string normalizedStructure = NormalizeSignature(structuralSignature);
        if (!string.IsNullOrEmpty(normalizedStructure))
        {
            string[] structuralMatches = recent
                .Where(item => NormalizeSignature(item.StructuralSignature) == normalizedStructure)
                .Select(LabelFor)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (structuralMatches.Length > 0)
            {
                warnings.Add(new PaletteRepeatWarning(
                    "structuralSignature",
                    normalizedStructure,
                    structuralMatches,
                    "The supplied structural signature matches recent work. Change the layout grammar, depth structure, or transition behavior before authoring."));
            }
        }

        return warnings;
    }

    public static PaletteHarmonyEvaluation EvaluatePalette(IEnumerable<string> hexArgbColors)
    {
        ArgumentNullException.ThrowIfNull(hexArgbColors);

        RgbColor[] rgb = hexArgbColors
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(RgbColor.FromHexArgb)
            .Distinct()
            .ToArray();
        if (rgb.Length == 0)
        {
            return new PaletteHarmonyEvaluation(
                0,
                0,
                SchemeName(HarmonyScheme.Monochromatic),
                1,
                1,
                1,
                1,
                0,
                0,
                0,
                true,
                ["No authored colors were available for harmony scoring."]);
        }

        HslColor[] hsl = rgb.Select(HslColor.FromRgb).ToArray();
        HslColor[] chromatic = hsl
            .Where(color => color.Saturation >= 0.08)
            .ToArray();
        HarmonyFit bestFit = chromatic.Length <= 1
            ? new HarmonyFit(HarmonyScheme.Monochromatic, chromatic.FirstOrDefault().Hue, 1)
            : Enum.GetValues<HarmonyScheme>()
                .Select(scheme => EvaluateHarmonyFit(chromatic, scheme))
                .OrderByDescending(fit => fit.Score)
                .First();

        double averageSaturation = hsl.Average(color => color.Saturation);
        double saturationRange = hsl.Max(color => color.Saturation) - hsl.Min(color => color.Saturation);
        double[] lumas = rgb.Select(RelativeLuminance).ToArray();
        double lumaRange = lumas.Max() - lumas.Min();
        double saturationScore = ScoreSaturationBalance(hsl, bestFit.Scheme == HarmonyScheme.Monochromatic);
        double lumaScore = ScoreLumaBalance(rgb);
        double score = Round((bestFit.Score * 0.55) + (saturationScore * 0.20) + (lumaScore * 0.25));
        bool harmonious = score >= 0.68
                          && bestFit.Score >= 0.55
                          && saturationScore >= 0.45
                          && lumaScore >= 0.45;

        string scheme = SchemeName(bestFit.Scheme);
        return new PaletteHarmonyEvaluation(
            rgb.Length,
            chromatic.Length,
            scheme,
            score,
            Round(bestFit.Score),
            Round(saturationScore),
            Round(lumaScore),
            Round(averageSaturation),
            Round(saturationRange),
            Round(lumaRange),
            harmonious,
            [
                $"Best hue-wheel fit: {scheme} from base hue {Round(NormalizeHue(bestFit.BaseHue))} deg.",
                $"Saturation avg/range: {Round(averageSaturation)}/{Round(saturationRange)}.",
                $"Luma range: {Round(lumaRange)}."
            ]);
    }

    public static double[] GetHueOffsets(HarmonyScheme scheme)
    {
        return scheme switch
        {
            HarmonyScheme.Analogous => [0, -30, 30],
            HarmonyScheme.Complementary => [0, 180],
            HarmonyScheme.SplitComplementary => [0, 150, 210],
            HarmonyScheme.Triadic => [0, 120, 240],
            HarmonyScheme.Tetradic => [0, 90, 180, 270],
            HarmonyScheme.Monochromatic => [0],
            _ => throw new ArgumentOutOfRangeException(nameof(scheme), scheme, null)
        };
    }

    public static HarmonyScheme ParseHarmonyScheme(string? value)
    {
        string normalized = NormalizeToken(value);
        return normalized switch
        {
            "" or "analogous" => HarmonyScheme.Analogous,
            "complementary" or "complement" => HarmonyScheme.Complementary,
            "splitcomplementary" or "splitcomplement" or "split" => HarmonyScheme.SplitComplementary,
            "triadic" or "triad" => HarmonyScheme.Triadic,
            "tetradic" or "rectangle" or "rectangular" => HarmonyScheme.Tetradic,
            "monochromatic" or "mono" or "monochrome" => HarmonyScheme.Monochromatic,
            _ => throw new ArgumentException(
                "Harmony scheme must be one of: analogous, complementary, split-complementary, triadic, tetradic, monochromatic.",
                nameof(value))
        };
    }

    public static TonalSeed ParseTonalSeed(string? value)
    {
        string normalized = NormalizeToken(value);
        return normalized switch
        {
            "" or "dark" or "deep" => TonalSeed.Dark,
            "light" or "bright" => TonalSeed.Light,
            "balanced" or "mid" or "neutral" => TonalSeed.Balanced,
            _ => throw new ArgumentException(
                "Tonal seed must be one of: dark, light, balanced.",
                nameof(value))
        };
    }

    public static HueBand GetHueBand(double hueDegrees)
    {
        double hue = NormalizeHue(hueDegrees);
        int index = Math.Clamp((int)Math.Floor(hue / 30.0), 0, 11);
        return new HueBand(index, s_hueBandNames[index], index * 30, index * 30 + 29.999);
    }

    public static double ContrastRatio(string foregroundHexArgb, string backgroundHexArgb)
    {
        RgbColor foreground = RgbColor.FromHexArgb(foregroundHexArgb);
        RgbColor background = RgbColor.FromHexArgb(backgroundHexArgb);
        return ContrastRatio(foreground, background);
    }

    internal static string SchemeName(HarmonyScheme scheme)
        => scheme switch
        {
            HarmonyScheme.SplitComplementary => "split-complementary",
            _ => NormalizeToken(scheme.ToString())
        };

    internal static string ToneName(TonalSeed tone)
        => NormalizeToken(tone.ToString());

    private static bool PaletteRolesContainHueBand(IReadOnlyList<string> roles, int band)
    {
        foreach (string role in roles)
        {
            foreach (int parsedBand in ParseHueBands(role))
            {
                if (parsedBand == band)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<int> ParseHueBands(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(text, @"(?i)\b(?:base[-\s]?hue|hue|h)\s*[:= ]\s*(\d+(?:\.\d+)?)"))
        {
            if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double hue))
            {
                yield return GetHueBand(hue).Index;
            }
        }

        foreach (Match match in Regex.Matches(text, @"#(?<hex>[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b"))
        {
            yield return GetHueBand(HslColor.FromRgb(RgbColor.FromHexArgb("#" + match.Groups["hex"].Value)).Hue).Index;
        }

        string normalized = text.ToLowerInvariant();
        foreach ((string name, int band) in s_hueNameBands)
        {
            if (Regex.IsMatch(normalized, $@"(?<![a-z0-9]){Regex.Escape(name)}(?![a-z0-9])", RegexOptions.IgnoreCase))
            {
                yield return band;
            }
        }
    }

    private static string LabelFor(CreativeDirectionFingerprint fingerprint)
    {
        return string.IsNullOrWhiteSpace(fingerprint.ConceptLabel)
            ? fingerprint.Timestamp.ToString("O", CultureInfo.InvariantCulture)
            : fingerprint.ConceptLabel.Trim();
    }

    private static string NormalizeSignature(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string[] tokens = Regex.Matches(value.ToLowerInvariant(), @"[a-z0-9]+")
            .Select(match => match.Value)
            .ToArray();
        return string.Join(" ", tokens);
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
    }

    private static double ResolveAccentHue(double baseHue, HarmonyScheme scheme, IReadOnlyList<HarmonyHue> harmonyHues)
    {
        return scheme switch
        {
            HarmonyScheme.Analogous => NormalizeHue(baseHue + 30),
            HarmonyScheme.Complementary => NormalizeHue(baseHue + 180),
            HarmonyScheme.SplitComplementary => NormalizeHue(baseHue + 210),
            HarmonyScheme.Triadic => NormalizeHue(baseHue + 120),
            HarmonyScheme.Tetradic => NormalizeHue(baseHue + 90),
            HarmonyScheme.Monochromatic => NormalizeHue(baseHue),
            _ => harmonyHues[^1].HueDegrees
        };
    }

    private static PaletteRoleColor ToRole(string role, HslColor color, string usage)
        => new(
            role,
            color.ToRgb().ToHexArgb(),
            Round(color.Hue),
            Round(color.Saturation),
            Round(color.Lightness),
            usage);

    private static PaletteContrastCheck Contrast(
        string foregroundRole,
        HslColor foreground,
        string backgroundRole,
        HslColor background,
        double floor)
    {
        double ratio = ContrastRatio(foreground.ToRgb(), background.ToRgb());
        return new PaletteContrastCheck(foregroundRole, backgroundRole, Round(ratio), floor, ratio + 0.0001 >= floor);
    }

    private static HslColor EnsureContrast(
        HslColor color,
        IReadOnlyList<HslColor> backgrounds,
        double floor,
        bool preferLight)
    {
        if (MinimumContrast(color, backgrounds) + 0.0001 >= floor)
        {
            return color;
        }

        HslColor best = color;
        double bestRatio = MinimumContrast(color, backgrounds);
        const int steps = 200;
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            double lightness = preferLight
                ? 0.98 - (0.98 * t)
                : 0.02 + (0.96 * t);
            var candidate = color with { Lightness = lightness };
            double ratio = MinimumContrast(candidate, backgrounds);
            if (ratio > bestRatio)
            {
                best = candidate;
                bestRatio = ratio;
            }

            if (ratio + 0.0001 >= floor)
            {
                return candidate;
            }
        }

        return best;
    }

    private static double MinimumContrast(HslColor color, IReadOnlyList<HslColor> backgrounds)
    {
        RgbColor foreground = color.ToRgb();
        return backgrounds
            .Select(background => ContrastRatio(foreground, background.ToRgb()))
            .DefaultIfEmpty(double.PositiveInfinity)
            .Min();
    }

    private static double ContrastRatio(RgbColor left, RgbColor right)
    {
        double l1 = RelativeLuminance(left);
        double l2 = RelativeLuminance(right);
        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static HarmonyFit EvaluateHarmonyFit(IReadOnlyList<HslColor> colors, HarmonyScheme scheme)
    {
        double[] offsets = GetHueOffsets(scheme);
        HarmonyFit best = new(scheme, colors[0].Hue, 0);
        foreach (HslColor candidateBase in colors)
        {
            double score = colors
                .Select(color => ScoreHueAgainstScheme(color.Hue, candidateBase.Hue, offsets, scheme))
                .Average();
            if (score > best.Score)
            {
                best = new HarmonyFit(scheme, candidateBase.Hue, score);
            }
        }

        return best;
    }

    private static double ScoreHueAgainstScheme(
        double hue,
        double baseHue,
        IReadOnlyList<double> offsets,
        HarmonyScheme scheme)
    {
        double closestDistance = offsets
            .Select(offset => HueDistance(hue, NormalizeHue(baseHue + offset)))
            .Min();
        double tolerance = scheme switch
        {
            HarmonyScheme.Monochromatic => 24,
            HarmonyScheme.Analogous => 42,
            _ => 36
        };
        return Math.Clamp(1 - (closestDistance / tolerance), 0, 1);
    }

    private static double HueDistance(double left, double right)
    {
        double delta = Math.Abs(NormalizeHue(left) - NormalizeHue(right));
        return Math.Min(delta, 360 - delta);
    }

    private static double ScoreSaturationBalance(IReadOnlyList<HslColor> colors, bool monochromaticFit)
    {
        double average = colors.Average(color => color.Saturation);
        double max = colors.Max(color => color.Saturation);
        double min = colors.Min(color => color.Saturation);
        double range = max - min;
        double oversaturatedPenalty = Math.Clamp((average - 0.68) / 0.22, 0, 1) * 0.55
                                      + Math.Clamp((max - 0.90) / 0.10, 0, 1) * 0.25;
        double flatPenalty = monochromaticFit ? 0 : Math.Clamp((0.12 - average) / 0.12, 0, 1) * 0.35;
        double rangePenalty = Math.Clamp((range - 0.75) / 0.25, 0, 1) * 0.20;
        return Math.Clamp(1 - oversaturatedPenalty - flatPenalty - rangePenalty, 0, 1);
    }

    private static double ScoreLumaBalance(IReadOnlyList<RgbColor> colors)
    {
        if (colors.Count <= 1)
        {
            return 1;
        }

        double[] lumas = colors.Select(RelativeLuminance).ToArray();
        double range = lumas.Max() - lumas.Min();
        if (range < 0.18)
        {
            return Math.Clamp(range / 0.18, 0, 1) * 0.55;
        }

        double highContrastPenalty = Math.Clamp((range - 0.86) / 0.14, 0, 1) * 0.10;
        return Math.Clamp(1 - highContrastPenalty, 0, 1);
    }

    private static double RelativeLuminance(RgbColor color)
    {
        static double Linear(double channel)
        {
            double c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static string HarmonyName(int index)
        => index switch
        {
            0 => "base",
            1 => "harmony-a",
            2 => "harmony-b",
            3 => "harmony-c",
            _ => $"harmony-{index}"
        };

    private static double ClampFinite(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static double NormalizeHue(double hue)
    {
        if (double.IsNaN(hue) || double.IsInfinity(hue))
        {
            return 0;
        }

        double result = hue % 360.0;
        return result < 0 ? result + 360.0 : result;
    }

    private static double NormalizeOffset(double offset)
    {
        double normalized = offset % 360.0;
        if (normalized > 180)
        {
            normalized -= 360;
        }
        else if (normalized <= -180)
        {
            normalized += 360;
        }

        return normalized;
    }

    private static double Round(double value)
        => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private sealed record RoleTone(
        double BackgroundBaseLightness,
        double BackgroundAccentLightness,
        double ForegroundLightness,
        double TextLightness,
        double AccentLightness,
        bool PreferLightObjects,
        double BackgroundSaturationFactor,
        double BackgroundAccentSaturationFactor,
        double ForegroundSaturationFactor)
    {
        public static RoleTone For(TonalSeed tone)
        {
            return tone switch
            {
                TonalSeed.Light => new RoleTone(0.96, 0.86, 0.24, 0.08, 0.34, false, 0.40, 0.55, 0.75),
                TonalSeed.Balanced => new RoleTone(0.16, 0.24, 0.78, 0.96, 0.66, true, 0.48, 0.65, 0.78),
                _ => new RoleTone(0.10, 0.18, 0.76, 0.96, 0.64, true, 0.48, 0.65, 0.78)
            };
        }
    }

    private readonly record struct HarmonyFit(HarmonyScheme Scheme, double BaseHue, double Score);

    private readonly record struct HslColor(double Hue, double Saturation, double Lightness)
    {
        public RgbColor ToRgb()
        {
            double h = NormalizeHue(Hue) / 360.0;
            double s = Math.Clamp(Saturation, 0, 1);
            double l = Math.Clamp(Lightness, 0, 1);

            double r;
            double g;
            double b;
            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                static double HueToRgb(double p, double q, double t)
                {
                    if (t < 0)
                    {
                        t += 1;
                    }

                    if (t > 1)
                    {
                        t -= 1;
                    }

                    if (t < 1.0 / 6.0)
                    {
                        return p + (q - p) * 6 * t;
                    }

                    if (t < 1.0 / 2.0)
                    {
                        return q;
                    }

                    if (t < 2.0 / 3.0)
                    {
                        return p + (q - p) * (2.0 / 3.0 - t) * 6;
                    }

                    return p;
                }

                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            return new RgbColor(
                (byte)Math.Round(r * 255, MidpointRounding.AwayFromZero),
                (byte)Math.Round(g * 255, MidpointRounding.AwayFromZero),
                (byte)Math.Round(b * 255, MidpointRounding.AwayFromZero));
        }

        public static HslColor FromRgb(RgbColor rgb)
        {
            double r = rgb.R / 255.0;
            double g = rgb.G / 255.0;
            double b = rgb.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double h;
            double s;
            double l = (max + min) / 2;

            if (Math.Abs(max - min) < 0.000001)
            {
                h = 0;
                s = 0;
            }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (Math.Abs(max - r) < 0.000001)
                {
                    h = (g - b) / d + (g < b ? 6 : 0);
                }
                else if (Math.Abs(max - g) < 0.000001)
                {
                    h = (b - r) / d + 2;
                }
                else
                {
                    h = (r - g) / d + 4;
                }

                h *= 60;
            }

            return new HslColor(h, s, l);
        }
    }

    private readonly record struct RgbColor(byte R, byte G, byte B)
    {
        public string ToHexArgb()
            => FormattableString.Invariant($"#ff{R:x2}{G:x2}{B:x2}");

        public static RgbColor FromHexArgb(string value)
        {
            string hex = value.Trim().TrimStart('#');
            if (hex.Length == 8)
            {
                hex = hex[2..];
            }

            if (hex.Length != 6)
            {
                throw new ArgumentException("Expected #RRGGBB or #AARRGGBB.", nameof(value));
            }

            return new RgbColor(
                byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
    }
}

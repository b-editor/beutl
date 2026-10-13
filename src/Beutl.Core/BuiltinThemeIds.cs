using System.Collections.Frozen;
using System.Globalization;

namespace Beutl;

// Persisted in settings.json (ViewConfig.Theme), so these ids never change; extensions pick their own. In Beutl.Core
// because settings normalization (Configuration) and theme registration (Extensibility) must agree on them.
public static class BuiltinThemeIds
{
    public const string Light = "light";
    public const string Dark = "dark";
    public const string HighContrast = "highcontrast";
    public const string System = "system";

    // Frozen rather than a HashSet behind IReadOnlySet: this gates ThemeRegistry's reserved-id
    // enforcement, so a caller must not be able to downcast and mutate it.
    public static FrozenSet<string> All { get; } =
        FrozenSet.ToFrozenSet([Light, Dark, HighContrast, System], StringComparer.Ordinal);

    /// <summary>
    /// The built-in id a legacy &lt;2.0 <c>ViewTheme</c> value names, or null for 1 (the pre-2.0 default: the user
    /// never chose) and values outside 0-3. The caller picks the fallback, an app-layer id that
    /// <see cref="IsReserved"/> must not reserve.
    /// </summary>
    public static string? TryFromLegacyEnum(int value) => value switch
    {
        0 => Light,
        2 => HighContrast,
        3 => System,
        _ => null,
    };

    /// <summary>
    /// The built-in id for a legacy &lt;2.0 form (0-3 or a PascalCase name), any other value trimmed as a custom id,
    /// or null for a blank value or one <see cref="TryFromLegacyEnum"/> rejects.
    /// </summary>
    public static string? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();

        // A hand-edited settings.json may quote the legacy number. Ids are otherwise arbitrary
        // strings, so one that merely looks numeric ("2026") is custom and must survive.
        if (int.TryParse(raw, CultureInfo.InvariantCulture, out int legacyEnum)
            && legacyEnum is >= 0 and <= 3)
        {
            return TryFromLegacyEnum(legacyEnum);
        }

        return raw.ToLowerInvariant() switch
        {
            Light => Light,
            Dark => Dark,
            HighContrast => HighContrast,
            System => System,
            _ => raw,
        };
    }

    /// <summary>
    /// True for a built-in id and any value settings normalization would not keep as it is, such as "Dark", "2", "1"
    /// or an untrimmed id. An extension must not register such an id: the next load would rewrite the user's choice.
    /// </summary>
    public static bool IsReserved(string? id) =>
        TryNormalize(id) is not { } normalized
        || !StringComparer.Ordinal.Equals(normalized, id)
        || All.Contains(normalized);
}

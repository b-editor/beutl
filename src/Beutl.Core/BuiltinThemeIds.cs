using System.Collections.Frozen;
using System.Globalization;

namespace Beutl;

// Persisted in settings.json (ViewConfig.Theme) — these ids must never change. Extensions pick their own.
// In Beutl.Core so Beutl.Configuration and Beutl.Extensibility both see it without a cycle: settings
// normalization and registry validation must agree on what counts as a built-in id, or an extension
// could register an id that settings then rewrites out from under it.
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
    /// The built-in id a legacy &lt;2.0 <c>ViewTheme</c> value names, or null when it names no theme:
    /// 1 (Dark) was the pre-2.0 default, so it marks a user who never chose a theme, and anything
    /// outside the enum's 0-3 is corrupt. The caller owns the fallback — the product default is an
    /// app-layer id this class must never return, since <see cref="IsReserved"/> backs ThemeRegistry's
    /// reserved-id check.
    /// </summary>
    public static string? TryFromLegacyEnum(int value) => value switch
    {
        0 => Light,
        2 => HighContrast,
        3 => System,
        _ => null,
    };

    /// <summary>
    /// The canonical id for a persisted value: legacy &lt;2.0 forms (the enum as 0-3, or a PascalCase
    /// name) become the stable id and anything else is a custom id returned trimmed. Null when the
    /// value names no theme — it is blank, or a legacy enum form
    /// <see cref="TryFromLegacyEnum"/> rejects — leaving the fallback to the caller.
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
    /// True when settings normalization would not hand <paramref name="id"/> back as it is — a
    /// built-in id, one of its legacy aliases ("Dark", "2"), or a value that names no theme at all
    /// ("1", ""). An extension must not register such an id: settings would rewrite the user's
    /// selection on the next load, silently dropping the extension's theme.
    /// </summary>
    public static bool IsReserved(string? id) =>
        TryNormalize(id) is not { } normalized || All.Contains(normalized);
}

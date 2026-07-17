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

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal) { Light, Dark, HighContrast, System };

    // <2.0 persisted the ViewTheme enum, whose only members were 0-3.
    public static string FromLegacyEnum(int value) => value switch
    {
        0 => Light,
        1 => Dark,
        2 => HighContrast,
        3 => System,
        _ => Dark,
    };

    /// <summary>
    /// The canonical id for a persisted value: legacy &lt;2.0 forms (the enum as 0-3, or a PascalCase
    /// name) become the stable id, anything else is a custom id returned trimmed, and an empty or
    /// missing value falls back to <see cref="Dark"/>.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Dark;
        }

        raw = raw.Trim();

        // A hand-edited settings.json may quote the legacy number. Ids are otherwise arbitrary
        // strings, so one that merely looks numeric ("2026") is custom and must survive.
        if (int.TryParse(raw, CultureInfo.InvariantCulture, out int legacyEnum)
            && legacyEnum is >= 0 and <= 3)
        {
            return FromLegacyEnum(legacyEnum);
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
    /// True when <see cref="Normalize"/> would turn <paramref name="id"/> into a built-in — a
    /// built-in id or one of its legacy aliases ("Dark", "2"). An extension must not register such an
    /// id: settings would rewrite the user's selection to the built-in on the next load, silently
    /// dropping the extension's theme.
    /// </summary>
    public static bool IsReserved(string? id) => All.Contains(Normalize(id));
}

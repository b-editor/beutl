namespace Beutl;

// Persisted in settings.json (ViewConfig.Theme) — these ids must never change. Extensions pick their own.
// In Beutl.Core so Beutl.Configuration and Beutl.Extensibility both see it without a cycle.
public static class BuiltinThemeIds
{
    public const string Light = "light";
    public const string Dark = "dark";
    public const string HighContrast = "highcontrast";
    public const string System = "system";
}

namespace Beutl;

// Theme ids Beutl ships that are not built-ins. The near-black design theme is registered by a
// ThemeExtension in the app layer, but ViewConfig's default and the PackageTools shell have to name it
// without depending on that layer, so the id lives here. Deliberately not in BuiltinThemeIds: that set
// is what an extension may NOT register, and this id is an extension's own.
public static class FirstPartyThemeIds
{
    // Persisted in settings.json (ViewConfig.Theme), so it must never change.
    public const string DarkBorder = "beutl.dark.border";
}

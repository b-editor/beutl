namespace Beutl;

// Theme ids Beutl ships that are not built-ins. ViewConfig's default and the PackageTools shell name
// the near-black design theme without depending on the app layer that registers it. Deliberately not
// in BuiltinThemeIds: that set is what an extension may NOT register, and this id is an extension's own.
public static class FirstPartyThemeIds
{
    // Persisted in settings.json (ViewConfig.Theme), so it must never change.
    public const string DarkBorder = "beutl.dark.border";
}

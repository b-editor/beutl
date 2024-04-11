// https://github.com/AvaloniaUI/Avalonia.Essentials/blob/main/src/Preferences/Preferences.uwp.cs

namespace Beutl.Configuration;

public static class Preferences
{
    static Preferences()
    {
        Default = new DefaultPreferences(Path.Combine(BeutlEnvironment.HomeVariable, "preferences.json"));
    }

    public static IPreferences Default { get; }
}

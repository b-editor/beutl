// https://github.com/AvaloniaUI/Avalonia.Essentials/blob/main/src/Preferences/Preferences.uwp.cs

namespace Beutl.Configuration;

public interface IPreferences
{
    void Set<T>(string key, T value);

    T Get<T>(string key, T defaultValue);
}

// https://github.com/AvaloniaUI/Avalonia.Essentials/blob/main/src/Preferences/Preferences.uwp.cs

namespace Beutl.Configuration;

public interface IPreferences
{
    bool ContainsKey(string key);

    void Remove(string key);

    void Clear();

    void Set<T>(string key, T value);

    T Get<T>(string key, T defaultValue);
}

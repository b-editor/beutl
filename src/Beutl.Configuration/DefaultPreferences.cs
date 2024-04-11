// https://github.com/AvaloniaUI/Avalonia.Essentials/blob/main/src/Preferences/Preferences.uwp.cs

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Beutl.Configuration;

public sealed class DefaultPreferences : IPreferences
{
    private static readonly Type[] s_supportedTypes =
    [
        typeof(bool),
        typeof(char),
        typeof(sbyte),
        typeof(byte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(DateTime),
        typeof(string),
    ];

    private readonly ConcurrentDictionary<string, string> _preferences = new();
    private readonly string _filePath;

    public DefaultPreferences(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public bool ContainsKey(string key)
    {
        return _preferences.ContainsKey(key);
    }

    public void Remove(string key)
    {
        _preferences.TryRemove(key, out _);
        Save();
    }

    public void Clear()
    {
        _preferences.Clear();
        Save();
    }

    public void Set<T>(string key, T value)
    {
        CheckIsSupportedType<T>();

        if (value is null)
            _preferences.TryRemove(key, out _);
        else
            _preferences[key] = string.Format(CultureInfo.InvariantCulture, "{0}", value);

        Save();
    }

    public T Get<T>(string key, T defaultValue)
    {
        CheckIsSupportedType<T>();

        if (_preferences.TryGetValue(key, out string? value) && value is not null)
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                // bad get, fall back to default
            }
        }

        return defaultValue;
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            using FileStream stream = File.OpenRead(_filePath);

            Dictionary<string, string>? readPreferences = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);

            if (readPreferences != null)
            {
                _preferences.Clear();
                foreach (KeyValuePair<string, string> pair in readPreferences)
                    _preferences.TryAdd(pair.Key, pair.Value);
            }
        }
        catch (JsonException)
        {
            // if deserialization fails proceed with empty settings
        }
    }

    private void Save()
    {
        string? dir = Path.GetDirectoryName(_filePath);
        Directory.CreateDirectory(dir!);

        using FileStream stream = File.Create(_filePath);
        JsonSerializer.Serialize(stream, _preferences);
    }

    internal static void CheckIsSupportedType<T>()
    {
        Type type = typeof(T);
        if (!s_supportedTypes.Contains(type))
        {
            throw new NotSupportedException($"Preferences using '{type}' type is not supported");
        }
    }
}

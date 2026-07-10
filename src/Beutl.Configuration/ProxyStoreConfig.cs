using System.ComponentModel.DataAnnotations;
using Beutl;

namespace Beutl.Configuration;

public sealed class ProxyStoreConfig : ConfigurationBase
{
    public const long MinTotalBytes = 5L * 1024 * 1024 * 1024;
    public const long DefaultTotalBytes = 50L * 1024 * 1024 * 1024;
    public const long MaxTotalBytesLimit = 500L * 1024 * 1024 * 1024;

    // Bounds of the closed Beutl.Media.Proxy.ProxyPreset int set (Half=1, Quarter=2, Eighth=3),
    // mirrored here without a Configuration -> Engine cycle. Default is Quarter.
    public const int MinPreset = 1;
    public const int MaxPreset = 3;
    public const int DefaultPresetValue = 2;
    public static string DefaultStoreRootPath => Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "proxies");

    public static readonly CoreProperty<string> StoreRootPathProperty;
    public static readonly CoreProperty<long> MaxTotalBytesProperty;
    public static readonly CoreProperty<int> DefaultPresetProperty;

    static ProxyStoreConfig()
    {
        StoreRootPathProperty = ConfigureProperty<string, ProxyStoreConfig>(nameof(StoreRootPath))
            .DefaultValue(DefaultStoreRootPath)
            .Register();

        MaxTotalBytesProperty = ConfigureProperty<long, ProxyStoreConfig>(nameof(MaxTotalBytes))
            .DefaultValue(DefaultTotalBytes)
            .Register();

        DefaultPresetProperty = ConfigureProperty<int, ProxyStoreConfig>(nameof(DefaultPreset))
            .DefaultValue(DefaultPresetValue)
            .Register();
    }

    public string StoreRootPath
    {
        // NormalizeStoreRootPath already returns an absolute path (or the default), so no further
        // Path.GetFullPath here — a second call could throw for a malformed persisted value, defeating the
        // startup fallback that reads this accessor during recovery.
        get => NormalizeStoreRootPath(GetValue(StoreRootPathProperty));
        set => SetValue(StoreRootPathProperty, NormalizeStoreRootPath(value));
    }

    [Range(MinTotalBytes, MaxTotalBytesLimit)]
    public long MaxTotalBytes
    {
        get => Math.Clamp(GetValue(MaxTotalBytesProperty), MinTotalBytes, MaxTotalBytesLimit);
        set => SetValue(MaxTotalBytesProperty, Math.Clamp(value, MinTotalBytes, MaxTotalBytesLimit));
    }

    [Range(MinPreset, MaxPreset)]
    public int DefaultPreset
    {
        get => Math.Clamp(GetValue(DefaultPresetProperty), MinPreset, MaxPreset);
        set => SetValue(DefaultPresetProperty, Math.Clamp(value, MinPreset, MaxPreset));
    }

    private static string NormalizeStoreRootPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultStoreRootPath;

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException or NotSupportedException or PathTooLongException)
        {
            // A malformed persisted value (invalid characters, a bad root) must not throw on every access;
            // degrade to the default so proxy-store init can proceed and the caller can repair the setting.
            return DefaultStoreRootPath;
        }
    }
}

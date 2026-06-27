using System.ComponentModel.DataAnnotations;
using Beutl;

namespace Beutl.Configuration;

public sealed class ProxyStoreConfig : ConfigurationBase
{
    public const long MinTotalBytes = 5L * 1024 * 1024 * 1024;
    public const long DefaultTotalBytes = 50L * 1024 * 1024 * 1024;
    public const long MaxTotalBytesLimit = 500L * 1024 * 1024 * 1024;

    public static readonly CoreProperty<string> StoreRootPathProperty;
    public static readonly CoreProperty<long> MaxTotalBytesProperty;
    public static readonly CoreProperty<int> DefaultPresetProperty;

    static ProxyStoreConfig()
    {
        StoreRootPathProperty = ConfigureProperty<string, ProxyStoreConfig>(nameof(StoreRootPath))
            .DefaultValue(Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "proxies"))
            .Register();

        MaxTotalBytesProperty = ConfigureProperty<long, ProxyStoreConfig>(nameof(MaxTotalBytes))
            .DefaultValue(DefaultTotalBytes)
            .Register();

        // Matches Beutl.Media.Proxy.ProxyPreset.Quarter without introducing a Configuration -> Engine cycle.
        DefaultPresetProperty = ConfigureProperty<int, ProxyStoreConfig>(nameof(DefaultPreset))
            .DefaultValue(2)
            .Register();
    }

    public string StoreRootPath
    {
        get => Path.GetFullPath(GetValue(StoreRootPathProperty));
        set => SetValue(StoreRootPathProperty, Path.GetFullPath(value));
    }

    [Range(MinTotalBytes, MaxTotalBytesLimit)]
    public long MaxTotalBytes
    {
        get => Math.Clamp(GetValue(MaxTotalBytesProperty), MinTotalBytes, MaxTotalBytesLimit);
        set => SetValue(MaxTotalBytesProperty, Math.Clamp(value, MinTotalBytes, MaxTotalBytesLimit));
    }

    public int DefaultPreset
    {
        get => GetValue(DefaultPresetProperty);
        set => SetValue(DefaultPresetProperty, value);
    }
}

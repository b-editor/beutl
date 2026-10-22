using System.ComponentModel;
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

    public ProxyStoreConfig()
    {
        ScanProperties<ProxyStoreConfig>();
    }

    public IProperty<string> StoreRootPathProperty { get; } = new ValueOnlyProperty<string>(DefaultStoreRootPath);

    [Range(MinTotalBytes, MaxTotalBytesLimit)]
    public IProperty<long> MaxTotalBytesProperty { get; } = new ValueOnlyProperty<long>(DefaultTotalBytes);

    [Range(MinPreset, MaxPreset)]
    public IProperty<int> DefaultPresetProperty { get; } = new ValueOnlyProperty<int>(DefaultPresetValue);

    public string StoreRootPath
    {
        // NormalizeStoreRootPath returns an absolute path; a second GetFullPath could throw for a malformed value.
        get => NormalizeStoreRootPath(StoreRootPathProperty.Value);
        set => StoreRootPathProperty.Value = NormalizeStoreRootPath(value);
    }

    public long MaxTotalBytes
    {
        get => Math.Clamp(MaxTotalBytesProperty.Value, MinTotalBytes, MaxTotalBytesLimit);
        set => MaxTotalBytesProperty.Value = Math.Clamp(value, MinTotalBytes, MaxTotalBytesLimit);
    }

    public int DefaultPreset
    {
        get => Math.Clamp(DefaultPresetProperty.Value, MinPreset, MaxPreset);
        set => DefaultPresetProperty.Value = Math.Clamp(value, MinPreset, MaxPreset);
    }

    // gib comes from a free-form TextBox and may be huge, NaN, or infinite; clamp in GiB
    // space so the long cast never sees an out-of-range product.
    public static long ClampTotalBytesFromGiB(double gib)
    {
        const double bytesPerGiB = 1024d * 1024d * 1024d;
        double minGiB = MinTotalBytes / bytesPerGiB;
        double maxGiB = MaxTotalBytesLimit / bytesPerGiB;

        // NaN is not orderable, so Math.Clamp would return it unchanged; +/-Infinity saturate.
        double safeGiB = double.IsNaN(gib) ? DefaultTotalBytes / bytesPerGiB : Math.Clamp(gib, minGiB, maxGiB);
        return (long)Math.Round(safeGiB * bytesPerGiB);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
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

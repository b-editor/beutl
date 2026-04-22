using System.ComponentModel;

namespace Beutl.Configuration;

public enum ProxyGenerationMode
{
    Disabled,
    Manual,
    Auto,
}

public enum ProxyPresetKind
{
    HalfH264,
    QuarterH264,
    ProResProxy,
}

public enum PreviewQuality
{
    Full,
    Half,
    Quarter,
}

public sealed class ProxyConfig : ConfigurationBase
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    public static readonly CoreProperty<ProxyGenerationMode> GenerationModeProperty;
    public static readonly CoreProperty<ProxyPresetKind> ActivePresetProperty;
    public static readonly CoreProperty<string> CacheDirectoryProperty;
    public static readonly CoreProperty<double> MaxCacheSizeMBProperty;
    public static readonly CoreProperty<int> MinWidthToGenerateProperty;
    public static readonly CoreProperty<int> MinBitrateKbpsToGenerateProperty;
    public static readonly CoreProperty<int> MaxParallelJobsProperty;
    public static readonly CoreProperty<PreviewQuality> PreviewQualityProperty;

    static ProxyConfig()
    {
        IsEnabledProperty = ConfigureProperty<bool, ProxyConfig>(nameof(IsEnabled))
            .DefaultValue(true)
            .Register();

        GenerationModeProperty = ConfigureProperty<ProxyGenerationMode, ProxyConfig>(nameof(GenerationMode))
            .DefaultValue(ProxyGenerationMode.Manual)
            .Register();

        ActivePresetProperty = ConfigureProperty<ProxyPresetKind, ProxyConfig>(nameof(ActivePreset))
            .DefaultValue(ProxyPresetKind.HalfH264)
            .Register();

        CacheDirectoryProperty = ConfigureProperty<string, ProxyConfig>(nameof(CacheDirectory))
            .DefaultValue(string.Empty)
            .Register();

        MaxCacheSizeMBProperty = ConfigureProperty<double, ProxyConfig>(nameof(MaxCacheSizeMB))
            .DefaultValue(20 * 1024d)
            .Register();

        MinWidthToGenerateProperty = ConfigureProperty<int, ProxyConfig>(nameof(MinWidthToGenerate))
            .DefaultValue(1920)
            .Register();

        MinBitrateKbpsToGenerateProperty = ConfigureProperty<int, ProxyConfig>(nameof(MinBitrateKbpsToGenerate))
            .DefaultValue(8000)
            .Register();

        MaxParallelJobsProperty = ConfigureProperty<int, ProxyConfig>(nameof(MaxParallelJobs))
            .DefaultValue(1)
            .Register();

        PreviewQualityProperty = ConfigureProperty<PreviewQuality, ProxyConfig>(nameof(PreviewQuality))
            .DefaultValue(PreviewQuality.Half)
            .Register();
    }

    public ProxyConfig()
    {
        if (string.IsNullOrEmpty(CacheDirectory))
        {
            CacheDirectory = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "proxy-cache");
        }
    }

    public bool IsEnabled
    {
        get => GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    public ProxyGenerationMode GenerationMode
    {
        get => GetValue(GenerationModeProperty);
        set => SetValue(GenerationModeProperty, value);
    }

    public ProxyPresetKind ActivePreset
    {
        get => GetValue(ActivePresetProperty);
        set => SetValue(ActivePresetProperty, value);
    }

    public string CacheDirectory
    {
        get => GetValue(CacheDirectoryProperty);
        set => SetValue(CacheDirectoryProperty, value);
    }

    public double MaxCacheSizeMB
    {
        get => GetValue(MaxCacheSizeMBProperty);
        set => SetValue(MaxCacheSizeMBProperty, value);
    }

    public int MinWidthToGenerate
    {
        get => GetValue(MinWidthToGenerateProperty);
        set => SetValue(MinWidthToGenerateProperty, value);
    }

    public int MinBitrateKbpsToGenerate
    {
        get => GetValue(MinBitrateKbpsToGenerateProperty);
        set => SetValue(MinBitrateKbpsToGenerateProperty, value);
    }

    public int MaxParallelJobs
    {
        get => GetValue(MaxParallelJobsProperty);
        set => SetValue(MaxParallelJobsProperty, value);
    }

    public PreviewQuality PreviewQuality
    {
        get => GetValue(PreviewQualityProperty);
        set => SetValue(PreviewQualityProperty, value);
    }

    public float GetRenderScale()
    {
        return PreviewQuality switch
        {
            PreviewQuality.Half => 0.5f,
            PreviewQuality.Quarter => 0.25f,
            _ => 1.0f,
        };
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}

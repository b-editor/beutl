using System.Collections.ObjectModel;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Media.Source.Proxy;
using Beutl.Services.Proxy;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class ProxySettingsPageViewModel : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<ProxySettingsPageViewModel>();
    private readonly ProxyConfig _proxyConfig;
    private readonly CompositeDisposable _disposables = [];

    public ProxySettingsPageViewModel()
    {
        _proxyConfig = GlobalConfiguration.Instance.ProxyConfig;

        IsEnabled = _proxyConfig.GetObservable(ProxyConfig.IsEnabledProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        IsEnabled.Subscribe(v => _proxyConfig.IsEnabled = v).DisposeWith(_disposables);

        GenerationMode = _proxyConfig.GetObservable(ProxyConfig.GenerationModeProperty)
            .Select(v => (int)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        GenerationMode.Subscribe(v => _proxyConfig.GenerationMode = (ProxyGenerationMode)v)
            .DisposeWith(_disposables);

        ActivePreset = _proxyConfig.GetObservable(ProxyConfig.ActivePresetProperty)
            .Select(v => (int)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        ActivePreset.Subscribe(v => _proxyConfig.ActivePreset = (ProxyPresetKind)v)
            .DisposeWith(_disposables);

        PreviewQuality = _proxyConfig.GetObservable(ProxyConfig.PreviewQualityProperty)
            .Select(v => (int)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        PreviewQuality.Subscribe(v => _proxyConfig.PreviewQuality = (PreviewQuality)v)
            .DisposeWith(_disposables);

        CacheDirectory = _proxyConfig.GetObservable(ProxyConfig.CacheDirectoryProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        CacheDirectory.Subscribe(v => _proxyConfig.CacheDirectory = v ?? string.Empty)
            .DisposeWith(_disposables);

        MaxCacheSizeMB = _proxyConfig.GetObservable(ProxyConfig.MaxCacheSizeMBProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        MaxCacheSizeMB.Subscribe(v => _proxyConfig.MaxCacheSizeMB = v)
            .DisposeWith(_disposables);

        MinWidthToGenerate = _proxyConfig.GetObservable(ProxyConfig.MinWidthToGenerateProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        MinWidthToGenerate.Subscribe(v => _proxyConfig.MinWidthToGenerate = v)
            .DisposeWith(_disposables);

        MaxParallelJobs = _proxyConfig.GetObservable(ProxyConfig.MaxParallelJobsProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        MaxParallelJobs.Subscribe(v => _proxyConfig.MaxParallelJobs = Math.Max(1, v))
            .DisposeWith(_disposables);

        CacheEntries = new ObservableCollection<ProxyCacheEntryViewModel>();
        TotalCacheSizeMB = new ReactivePropertySlim<double>(0).DisposeWith(_disposables);
        IsFFmpegAvailable = new ReactivePropertySlim<bool>(ProxyGenerator.IsAvailable())
            .DisposeWith(_disposables);

        RefreshEntries();

        ProxyGenerationQueue.Instance.JobChanged
            .Subscribe(_ => RefreshEntries())
            .DisposeWith(_disposables);
    }

    public ReactiveProperty<bool> IsEnabled { get; }

    public ReactiveProperty<int> GenerationMode { get; }

    public ReactiveProperty<int> ActivePreset { get; }

    public ReactiveProperty<int> PreviewQuality { get; }

    public ReactiveProperty<string?> CacheDirectory { get; }

    public ReactiveProperty<double> MaxCacheSizeMB { get; }

    public ReactiveProperty<int> MinWidthToGenerate { get; }

    public ReactiveProperty<int> MaxParallelJobs { get; }

    public ObservableCollection<ProxyCacheEntryViewModel> CacheEntries { get; }

    public ReactivePropertySlim<double> TotalCacheSizeMB { get; }

    public ReactivePropertySlim<bool> IsFFmpegAvailable { get; }

    public void DeleteAllCache()
    {
        try
        {
            foreach (var entry in CacheEntries.ToArray())
            {
                ProxyCacheManager.Instance.Delete(entry.OriginalPath);
            }
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Failed to clear proxy cache.");
        }

        RefreshEntries();
    }

    public void DeleteEntry(ProxyCacheEntryViewModel item)
    {
        try
        {
            ProxyCacheManager.Instance.Delete(item.OriginalPath);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Failed to delete proxy entry: {Path}", item.OriginalPath);
        }

        RefreshEntries();
    }

    public void RegenerateEntry(ProxyCacheEntryViewModel item)
    {
        try
        {
            ProxyCacheManager.Instance.Invalidate(item.OriginalPath);
            ProxyGenerationQueue.Instance.Enqueue(item.OriginalPath, _proxyConfig.ActivePreset);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Failed to regenerate proxy entry: {Path}", item.OriginalPath);
        }

        RefreshEntries();
    }

    private void RefreshEntries()
    {
        try
        {
            var entries = ProxyCacheManager.Instance.Enumerate().ToArray();
            CacheEntries.Clear();
            foreach (var e in entries)
            {
                CacheEntries.Add(new ProxyCacheEntryViewModel(e));
            }

            long total = ProxyCacheManager.Instance.GetTotalSizeBytes();
            TotalCacheSizeMB.Value = total / (1024d * 1024d);
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Failed to refresh proxy cache entries.");
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

public sealed class ProxyCacheEntryViewModel
{
    public ProxyCacheEntryViewModel(ProxyEntry entry)
    {
        OriginalPath = entry.OriginalPath;
        DisplayName = Path.GetFileName(entry.OriginalPath);
        Preset = entry.Preset.ToString();
        SizeMB = entry.ProxyFileSize / (1024d * 1024d);
        Resolution = $"{entry.ProxyFrameSize.Width}x{entry.ProxyFrameSize.Height}";
        GeneratedAt = entry.GeneratedAt.ToLocalTime();
    }

    public string OriginalPath { get; }

    public string DisplayName { get; }

    public string Preset { get; }

    public double SizeMB { get; }

    public string Resolution { get; }

    public DateTime GeneratedAt { get; }
}

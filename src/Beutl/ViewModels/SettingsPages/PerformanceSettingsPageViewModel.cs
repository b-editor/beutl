using Beutl.Configuration;
using Beutl.Graphics.Backend;
using Beutl.Media.Proxy;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class PerformanceSettingsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public PerformanceSettingsPageViewModel(GlobalConfiguration configuration)
    {
        EditorConfig editorConfig = configuration.EditorConfig;
        ProxyStoreConfig proxyStore = configuration.ProxyStoreConfig;

        IsFrameCacheEnabled = SettingsPageBinding.Bind(editorConfig.IsFrameCacheEnabled, _disposables);
        FrameCacheMaxSize = SettingsPageBinding.Bind(editorConfig.FrameCacheMaxSize, _disposables);
        FrameCacheScale = SettingsPageBinding.Bind(
            editorConfig.FrameCacheScale.GetObservable().Select(v => (int)v),
            index => editorConfig.FrameCacheScale.Value = (FrameCacheConfigScale)index,
            _disposables);
        FrameCacheColorType = SettingsPageBinding.Bind(
            editorConfig.FrameCacheColorType.GetObservable().Select(v => (int)v),
            index => editorConfig.FrameCacheColorType.Value = (FrameCacheConfigColorType)index,
            _disposables);

        IsNodeCacheEnabled = SettingsPageBinding.Bind(editorConfig.IsNodeCacheEnabled, _disposables);
        NodeCacheMaxPixels = SettingsPageBinding.Bind(editorConfig.NodeCacheMaxPixels, _disposables);
        NodeCacheMinPixels = SettingsPageBinding.Bind(editorConfig.NodeCacheMinPixels, _disposables);

        ProxyStoreRootPath = new ReactiveProperty<string>(proxyStore.StoreRootPath.Value)
            .DisposeWith(_disposables);
        proxyStore.StoreRootPath.GetObservable()
            .Subscribe(path =>
            {
                string value = path ?? string.Empty;
                if (ProxyStoreRootPath.Value != value)
                {
                    ProxyStoreRootPath.Value = value;
                }
            })
            .DisposeWith(_disposables);
        ProxyStoreRootPath
            .Subscribe(path =>
            {
                string normalized = string.IsNullOrWhiteSpace(path)
                    ? ProxyStoreConfig.DefaultStoreRootPath
                    : path;
                if (proxyStore.StoreRootPath.Value != normalized)
                {
                    proxyStore.StoreRootPath.Value = normalized;
                }
            })
            .DisposeWith(_disposables);

        ProxyStoreMaxTotalGiB = proxyStore.MaxTotalBytes.GetObservable()
            .Select(static value => value / 1024d / 1024d / 1024d)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        ProxyStoreMaxTotalGiB.Subscribe(value =>
            {
                long bytes = ProxyStoreConfig.ClampTotalBytesFromGiB(value);
                proxyStore.MaxTotalBytes.Value = bytes;
                // If the clamp left the config unchanged (e.g. already at the cap), no change
                // notification fires to drive the bound value back, so re-sync it here.
                double clampedGiB = bytes / 1024d / 1024d / 1024d;
                if (ProxyStoreMaxTotalGiB.Value != clampedGiB)
                {
                    ProxyStoreMaxTotalGiB.Value = clampedGiB;
                }
            })
            .DisposeWith(_disposables);

        ProxyDefaultPreset = SettingsPageBinding.Bind(
            proxyStore.DefaultPreset.GetObservable()
                .Select(static value => Enum.IsDefined(typeof(ProxyPreset), value)
                    ? (ProxyPreset)value
                    : ProxyPreset.Quarter),
            preset => proxyStore.DefaultPreset.Value = (int)preset,
            _disposables);

        GraphicsConfig graphicsConfig = configuration.GraphicsConfig;
        var availableGpus = GraphicsContextFactory.GetAvailableDevices();

        var gpuItems = new List<GpuItem> { new(null, SettingsStrings.SelectedGpu_Auto) };
        gpuItems.AddRange(availableGpus.Select(g => new GpuItem(g.Name, g.Name)));
        AvailableGpus = gpuItems;

        string? savedGpuName = graphicsConfig.SelectedGpuName.Value;
        int selectedIndex = 0;
        if (!string.IsNullOrEmpty(savedGpuName))
        {
            int index = gpuItems.FindIndex(g => g.Name == savedGpuName);
            if (index >= 0)
            {
                selectedIndex = index;
            }
        }

        SelectedGpuIndex = new ReactiveProperty<int>(selectedIndex).DisposeWith(_disposables);
        SelectedGpuIndex.Subscribe(index =>
        {
            if (index >= 0 && index < AvailableGpus.Count)
            {
                graphicsConfig.SelectedGpuName.Value = AvailableGpus[index].Name;
            }
        }).DisposeWith(_disposables);
    }

    public ReactiveProperty<bool> IsFrameCacheEnabled { get; }

    public ReactiveProperty<double> FrameCacheMaxSize { get; }

    public ReactiveProperty<int> FrameCacheScale { get; }

    public ReactiveProperty<int> FrameCacheColorType { get; }

    public ReactiveProperty<bool> IsNodeCacheEnabled { get; }

    public ReactiveProperty<int> NodeCacheMaxPixels { get; }

    public ReactiveProperty<int> NodeCacheMinPixels { get; }

    public ReactiveProperty<string> ProxyStoreRootPath { get; }

    public ReactiveProperty<double> ProxyStoreMaxTotalGiB { get; }

    public ReactiveProperty<ProxyPreset> ProxyDefaultPreset { get; }

    public IReadOnlyList<ProxyPreset> ProxyPresetOptions { get; } = Enum.GetValues<ProxyPreset>();

    public IReadOnlyList<GpuItem> AvailableGpus { get; }

    public ReactiveProperty<int> SelectedGpuIndex { get; }

    public void Dispose() => _disposables.Dispose();
}

public record GpuItem(string? Name, string DisplayName);

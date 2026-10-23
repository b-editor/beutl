using Beutl.Configuration;
using Beutl.Graphics.Backend;
using Beutl.Media.Proxy;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class EditorSettingsPageViewModel : IDisposable
{
    private readonly ViewConfig _viewConfig;
    private readonly EditorConfig _editorConfig;
    private readonly GraphicsConfig _graphicsConfig;
    private readonly ProxyStoreConfig _proxyStoreConfig;
    private readonly VersionControlConfig _versionControlConfig;
    private readonly CompositeDisposable _disposables = [];

    public EditorSettingsPageViewModel()
    {
        _viewConfig = GlobalConfiguration.Instance.ViewConfig;
        _editorConfig = GlobalConfiguration.Instance.EditorConfig;
        _graphicsConfig = GlobalConfiguration.Instance.GraphicsConfig;
        _proxyStoreConfig = GlobalConfiguration.Instance.ProxyStoreConfig;
        _versionControlConfig = GlobalConfiguration.Instance.VersionControlConfig;

        AutoAdjustSceneDuration = CreateSetting(
            _editorConfig.AutoAdjustSceneDuration.GetObservable(),
            b => _editorConfig.AutoAdjustSceneDuration.Value = b);

        ShowExactBoundaries = CreateSetting(
            _viewConfig.ShowExactBoundaries.GetObservable(),
            b => _viewConfig.ShowExactBoundaries.Value = b);

        IsFrameCacheEnabled = CreateSetting(
            _editorConfig.IsFrameCacheEnabled.GetObservable(),
            b => _editorConfig.IsFrameCacheEnabled.Value = b);

        FrameCacheMaxSize = CreateSetting(
            _editorConfig.FrameCacheMaxSize.GetObservable(),
            b => _editorConfig.FrameCacheMaxSize.Value = b);

        FrameCacheScale = CreateSetting(
            _editorConfig.FrameCacheScale.GetObservable()
                .Select(v => (int)v),
            b => _editorConfig.FrameCacheScale.Value = (FrameCacheConfigScale)b);

        FrameCacheColorType = CreateSetting(
            _editorConfig.FrameCacheColorType.GetObservable()
                .Select(v => (int)v),
            b => _editorConfig.FrameCacheColorType.Value = (FrameCacheConfigColorType)b);

        IsNodeCacheEnabled = CreateSetting(
            _editorConfig.IsNodeCacheEnabled.GetObservable(),
            b => _editorConfig.IsNodeCacheEnabled.Value = b);

        NodeCacheMaxPixels = CreateSetting(
            _editorConfig.NodeCacheMaxPixels.GetObservable(),
            b => _editorConfig.NodeCacheMaxPixels.Value = b);

        NodeCacheMinPixels = CreateSetting(
            _editorConfig.NodeCacheMinPixels.GetObservable(),
            b => _editorConfig.NodeCacheMinPixels.Value = b);

        EnablePointerLockInProperty = CreateSetting(
            _editorConfig.EnablePointerLockInProperty.GetObservable(),
            b => _editorConfig.EnablePointerLockInProperty.Value = b);

        SwapTimelineScrollDirection = CreateSetting(
            _editorConfig.SwapTimelineScrollDirection.GetObservable(),
            b => _editorConfig.SwapTimelineScrollDirection.Value = b);

        ClampResizeToOriginalLength = CreateSetting(
            _editorConfig.ClampResizeToOriginalLength.GetObservable(),
            b => _editorConfig.ClampResizeToOriginalLength.Value = b);

        TimelineAutoScrollMode = CreateSetting(
            _editorConfig.TimelineAutoScrollMode.GetObservable()
                .Select(v => (int)v),
            b => _editorConfig.TimelineAutoScrollMode.Value = (TimelineAutoScrollMode)b);

        ToneMappingMode = CreateSetting(
            _editorConfig.ToneMappingMode.GetObservable()
                .Select(v => (int)v),
            b => _editorConfig.ToneMappingMode.Value = (UIToneMappingOperator)b);

        ToneMappingExposure = CreateSetting(
            _editorConfig.ToneMappingExposure.GetObservable()
                .Select(v => (double)v),
            b => _editorConfig.ToneMappingExposure.Value = (float)b);

        UseHdrPreview = CreateSetting(
            _editorConfig.UseHdrPreview.GetObservable(),
            b => _editorConfig.UseHdrPreview.Value = b);

        PreviewSourceMode = CreateSetting(
            _editorConfig.PreviewSourceMode.GetObservable()
                .Select(v => (int)v),
            _editorConfig.SetPreviewSourceModeFromIndex);

        ProxyStoreRootPath = new ReactiveProperty<string>(_proxyStoreConfig.StoreRootPath.Value)
            .DisposeWith(_disposables);
        _proxyStoreConfig.StoreRootPath.GetObservable()
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
                if (_proxyStoreConfig.StoreRootPath.Value != normalized)
                {
                    _proxyStoreConfig.StoreRootPath.Value = normalized;
                }
            })
            .DisposeWith(_disposables);

        ProxyStoreMaxTotalGiB = _proxyStoreConfig.MaxTotalBytes.GetObservable()
            .Select(static value => value / 1024d / 1024d / 1024d)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        ProxyStoreMaxTotalGiB.Subscribe(value =>
            {
                long bytes = ProxyStoreConfig.ClampTotalBytesFromGiB(value);
                _proxyStoreConfig.MaxTotalBytes.Value = bytes;
                // If the clamp left the config unchanged (e.g. already at the cap), no change
                // notification fires to drive the bound value back, so re-sync it here.
                double clampedGiB = bytes / 1024d / 1024d / 1024d;
                if (ProxyStoreMaxTotalGiB.Value != clampedGiB)
                {
                    ProxyStoreMaxTotalGiB.Value = clampedGiB;
                }
            })
            .DisposeWith(_disposables);

        ProxyDefaultPreset = CreateSetting(
            _proxyStoreConfig.DefaultPreset.GetObservable()
                .Select(static value => Enum.IsDefined(typeof(ProxyPreset), value)
                    ? (ProxyPreset)value
                    : ProxyPreset.Quarter),
            preset => _proxyStoreConfig.DefaultPreset.Value = (int)preset);

        EnableVersionControlForNewProjects = CreateSetting(
            _versionControlConfig
                .EnableForNewProjects.GetObservable(),
            value => _versionControlConfig.EnableForNewProjects.Value = value);

        AutoCommitOnSave = CreateSetting(
            _versionControlConfig
                .AutoCommitOnSave.GetObservable(),
            value => _versionControlConfig.AutoCommitOnSave.Value = value);

        AutoCommitOnClose = CreateSetting(
            _versionControlConfig
                .AutoCommitOnClose.GetObservable(),
            value => _versionControlConfig.AutoCommitOnClose.Value = value);

        GitExecutablePath = _versionControlConfig
            .GitExecutablePath.GetObservable()
            .Select(static value => value ?? string.Empty)
            .ToReactiveProperty(initialValue: string.Empty)
            .DisposeWith(_disposables);
        GitExecutablePath.Subscribe(value =>
            {
                string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (_versionControlConfig.GitExecutablePath.Value != normalized)
                {
                    _versionControlConfig.GitExecutablePath.Value = normalized;
                }
            })
            .DisposeWith(_disposables);

        UseLfsWhenAvailable = CreateSetting(
            _versionControlConfig
                .UseLfsWhenAvailable.GetObservable(),
            value => _versionControlConfig.UseLfsWhenAvailable.Value = value);

        LargeMediaWarningThresholdMb = _versionControlConfig
            .LargeMediaWarningThresholdMb.GetObservable()
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        LargeMediaWarningThresholdMb.Subscribe(value =>
            {
                int normalized = Math.Max(1, value);
                if (_versionControlConfig.LargeMediaWarningThresholdMb.Value != normalized)
                {
                    _versionControlConfig.LargeMediaWarningThresholdMb.Value = normalized;
                }

                if (LargeMediaWarningThresholdMb.Value != normalized)
                {
                    LargeMediaWarningThresholdMb.Value = normalized;
                }
            })
            .DisposeWith(_disposables);

        InitializeGpuSelection();
    }

    private ReactiveProperty<T> CreateSetting<T>(IObservable<T> source, Action<T> write)
        where T : struct
    {
        ReactiveProperty<T> property = source
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        property.Subscribe(write)
            .DisposeWith(_disposables);
        return property;
    }

    private void InitializeGpuSelection()
    {
        var availableGpus = GraphicsContextFactory.GetAvailableDevices();

        var gpuItems = new List<GpuItem> { new(null, SettingsStrings.SelectedGpu_Auto) };
        gpuItems.AddRange(availableGpus.Select(g => new GpuItem(g.Name, g.Name)));
        AvailableGpus = gpuItems;

        string? savedGpuName = _graphicsConfig.SelectedGpuName.Value;
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
                _graphicsConfig.SelectedGpuName.Value = AvailableGpus[index].Name;
            }
        }).DisposeWith(_disposables);
    }

    public ReactiveProperty<bool> AutoAdjustSceneDuration { get; }

    public ReactiveProperty<bool> ShowExactBoundaries { get; }

    public ReactiveProperty<bool> IsFrameCacheEnabled { get; }

    public ReactiveProperty<double> FrameCacheMaxSize { get; }

    public ReactiveProperty<int> FrameCacheScale { get; }

    public ReactiveProperty<int> FrameCacheColorType { get; }

    public ReactiveProperty<bool> EnablePointerLockInProperty { get; }

    public ReactiveProperty<bool> IsNodeCacheEnabled { get; }

    public ReactiveProperty<int> NodeCacheMaxPixels { get; }

    public ReactiveProperty<int> NodeCacheMinPixels { get; }

    public ReactiveProperty<bool> SwapTimelineScrollDirection { get; }

    public ReactiveProperty<bool> ClampResizeToOriginalLength { get; }

    public ReactiveProperty<int> TimelineAutoScrollMode { get; }

    public ReactiveProperty<int> ToneMappingMode { get; }

    public ReactiveProperty<double> ToneMappingExposure { get; }

    public ReactiveProperty<bool> UseHdrPreview { get; }

    public ReactiveProperty<int> PreviewSourceMode { get; }

    public ReactiveProperty<string> ProxyStoreRootPath { get; }

    public ReactiveProperty<double> ProxyStoreMaxTotalGiB { get; }

    public ReactiveProperty<ProxyPreset> ProxyDefaultPreset { get; }

    public ReactiveProperty<bool> EnableVersionControlForNewProjects { get; }

    public ReactiveProperty<bool> AutoCommitOnSave { get; }

    public ReactiveProperty<bool> AutoCommitOnClose { get; }

    public ReactiveProperty<string> GitExecutablePath { get; }

    public ReactiveProperty<bool> UseLfsWhenAvailable { get; }

    public ReactiveProperty<int> LargeMediaWarningThresholdMb { get; }

    public IReadOnlyList<ProxyPreset> ProxyPresetOptions { get; } = Enum.GetValues<ProxyPreset>();

    public IReadOnlyList<GpuItem> AvailableGpus { get; private set; } = [];

    public ReactiveProperty<int> SelectedGpuIndex { get; private set; } = null!;

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

public record GpuItem(string? Name, string DisplayName);

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
            _editorConfig.GetObservable(EditorConfig.AutoAdjustSceneDurationProperty),
            b => _editorConfig.AutoAdjustSceneDuration = b);

        ShowExactBoundaries = CreateSetting(
            _viewConfig.GetObservable(ViewConfig.ShowExactBoundariesProperty),
            b => _viewConfig.ShowExactBoundaries = b);

        IsFrameCacheEnabled = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.IsFrameCacheEnabledProperty),
            b => _editorConfig.IsFrameCacheEnabled = b);

        FrameCacheMaxSize = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.FrameCacheMaxSizeProperty),
            b => _editorConfig.FrameCacheMaxSize = b);

        FrameCacheScale = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.FrameCacheScaleProperty)
                .Select(v => (int)v),
            b => _editorConfig.FrameCacheScale = (FrameCacheConfigScale)b);

        FrameCacheColorType = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.FrameCacheColorTypeProperty)
                .Select(v => (int)v),
            b => _editorConfig.FrameCacheColorType = (FrameCacheConfigColorType)b);

        IsNodeCacheEnabled = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.IsNodeCacheEnabledProperty),
            b => _editorConfig.IsNodeCacheEnabled = b);

        NodeCacheMaxPixels = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.NodeCacheMaxPixelsProperty),
            b => _editorConfig.NodeCacheMaxPixels = b);

        NodeCacheMinPixels = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.NodeCacheMinPixelsProperty),
            b => _editorConfig.NodeCacheMinPixels = b);

        EnablePointerLockInProperty = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.EnablePointerLockInPropertyProperty),
            b => _editorConfig.EnablePointerLockInProperty = b);

        SwapTimelineScrollDirection = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.SwapTimelineScrollDirectionProperty),
            b => _editorConfig.SwapTimelineScrollDirection = b);

        ClampResizeToOriginalLength = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.ClampResizeToOriginalLengthProperty),
            b => _editorConfig.ClampResizeToOriginalLength = b);

        TimelineAutoScrollMode = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.TimelineAutoScrollModeProperty)
                .Select(v => (int)v),
            b => _editorConfig.TimelineAutoScrollMode = (TimelineAutoScrollMode)b);

        ToneMappingMode = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.ToneMappingModeProperty)
                .Select(v => (int)v),
            b => _editorConfig.ToneMappingMode = (UIToneMappingOperator)b);

        ToneMappingExposure = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.ToneMappingExposureProperty)
                .Select(v => (double)v),
            b => _editorConfig.ToneMappingExposure = (float)b);

        UseHdrPreview = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.UseHdrPreviewProperty),
            b => _editorConfig.UseHdrPreview = b);

        PreviewSourceMode = CreateSetting(
            _editorConfig.GetObservable(EditorConfig.PreviewSourceModeProperty)
                .Select(v => (int)v),
            _editorConfig.SetPreviewSourceModeFromIndex);

        ProxyStoreRootPath = new ReactiveProperty<string>(_proxyStoreConfig.StoreRootPath)
            .DisposeWith(_disposables);
        _proxyStoreConfig.GetObservable(ProxyStoreConfig.StoreRootPathProperty)
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
                if (_proxyStoreConfig.StoreRootPath != normalized)
                {
                    _proxyStoreConfig.StoreRootPath = normalized;
                }
            })
            .DisposeWith(_disposables);

        ProxyStoreMaxTotalGiB = _proxyStoreConfig.GetObservable(ProxyStoreConfig.MaxTotalBytesProperty)
            .Select(static value => value / 1024d / 1024d / 1024d)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        ProxyStoreMaxTotalGiB.Subscribe(value =>
            {
                long bytes = ProxyStoreConfig.ClampTotalBytesFromGiB(value);
                _proxyStoreConfig.MaxTotalBytes = bytes;
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
            _proxyStoreConfig.GetObservable(ProxyStoreConfig.DefaultPresetProperty)
                .Select(static value => Enum.IsDefined(typeof(ProxyPreset), value)
                    ? (ProxyPreset)value
                    : ProxyPreset.Quarter),
            preset => _proxyStoreConfig.DefaultPreset = (int)preset);

        EnableVersionControlForNewProjects = CreateSetting(
            _versionControlConfig
                .GetObservable(VersionControlConfig.EnableForNewProjectsProperty),
            value => _versionControlConfig.EnableForNewProjects = value);

        AutoCommitOnSave = CreateSetting(
            _versionControlConfig
                .GetObservable(VersionControlConfig.AutoCommitOnSaveProperty),
            value => _versionControlConfig.AutoCommitOnSave = value);

        AutoCommitOnClose = CreateSetting(
            _versionControlConfig
                .GetObservable(VersionControlConfig.AutoCommitOnCloseProperty),
            value => _versionControlConfig.AutoCommitOnClose = value);

        GitExecutablePath = _versionControlConfig
            .GetObservable(VersionControlConfig.GitExecutablePathProperty)
            .Select(static value => value ?? string.Empty)
            .ToReactiveProperty(initialValue: string.Empty)
            .DisposeWith(_disposables);
        GitExecutablePath.Subscribe(value =>
            {
                string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (_versionControlConfig.GitExecutablePath != normalized)
                {
                    _versionControlConfig.GitExecutablePath = normalized;
                }
            })
            .DisposeWith(_disposables);

        UseLfsWhenAvailable = CreateSetting(
            _versionControlConfig
                .GetObservable(VersionControlConfig.UseLfsWhenAvailableProperty),
            value => _versionControlConfig.UseLfsWhenAvailable = value);

        LargeMediaWarningThresholdMb = _versionControlConfig
            .GetObservable(VersionControlConfig.LargeMediaWarningThresholdMbProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        LargeMediaWarningThresholdMb.Subscribe(value =>
            {
                int normalized = Math.Max(1, value);
                if (_versionControlConfig.LargeMediaWarningThresholdMb != normalized)
                {
                    _versionControlConfig.LargeMediaWarningThresholdMb = normalized;
                }

                if (LargeMediaWarningThresholdMb.Value != normalized)
                {
                    LargeMediaWarningThresholdMb.Value = normalized;
                }
            })
            .DisposeWith(_disposables);

        // GPU selection
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

        // Build GPU list with "Auto" as first item
        var gpuItems = new List<GpuItem> { new(null, SettingsStrings.SelectedGpu_Auto) };
        gpuItems.AddRange(availableGpus.Select(g => new GpuItem(g.Name, g.Name)));
        AvailableGpus = gpuItems;

        // Find current selection
        string? savedGpuName = _graphicsConfig.SelectedGpuName;
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
                _graphicsConfig.SelectedGpuName = AvailableGpus[index].Name;
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

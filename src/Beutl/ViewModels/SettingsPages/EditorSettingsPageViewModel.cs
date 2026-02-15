using Beutl.Configuration;
using Beutl.Graphics.Backend;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class EditorSettingsPageViewModel : IDisposable
{
    private readonly ViewConfig _viewConfig;
    private readonly EditorConfig _editorConfig;
    private readonly GraphicsConfig _graphicsConfig;
    private readonly CompositeDisposable _disposables = [];

    public EditorSettingsPageViewModel()
    {
        _viewConfig = GlobalConfiguration.Instance.ViewConfig;
        _editorConfig = GlobalConfiguration.Instance.EditorConfig;
        _graphicsConfig = GlobalConfiguration.Instance.GraphicsConfig;

        AutoAdjustSceneDuration = _editorConfig.GetObservable(EditorConfig.AutoAdjustSceneDurationProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        AutoAdjustSceneDuration.Subscribe(b => _editorConfig.AutoAdjustSceneDuration = b)
            .DisposeWith(_disposables);

        EnableAutoSave = _editorConfig.GetObservable(EditorConfig.IsAutoSaveEnabledProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        EnableAutoSave.Subscribe(b => _editorConfig.IsAutoSaveEnabled = b)
            .DisposeWith(_disposables);

        ShowExactBoundaries = _viewConfig.GetObservable(ViewConfig.ShowExactBoundariesProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        ShowExactBoundaries.Subscribe(b => _viewConfig.ShowExactBoundaries = b)
            .DisposeWith(_disposables);

        IsFrameCacheEnabled = _editorConfig.GetObservable(EditorConfig.IsFrameCacheEnabledProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        IsFrameCacheEnabled.Subscribe(b => _editorConfig.IsFrameCacheEnabled = b)
            .DisposeWith(_disposables);

        FrameCacheMaxSize = _editorConfig.GetObservable(EditorConfig.FrameCacheMaxSizeProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        FrameCacheMaxSize.Subscribe(b => _editorConfig.FrameCacheMaxSize = b)
            .DisposeWith(_disposables);

        FrameCacheScale = _editorConfig.GetObservable(EditorConfig.FrameCacheScaleProperty)
            .Select(v => (int)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        FrameCacheScale.Subscribe(b => _editorConfig.FrameCacheScale = (FrameCacheConfigScale)b)
            .DisposeWith(_disposables);

        FrameCacheColorType = _editorConfig.GetObservable(EditorConfig.FrameCacheColorTypeProperty)
            .Select(v => (int)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        FrameCacheColorType.Subscribe(b => _editorConfig.FrameCacheColorType = (FrameCacheConfigColorType)b)
            .DisposeWith(_disposables);

        IsNodeCacheEnabled = _editorConfig.GetObservable(EditorConfig.IsNodeCacheEnabledProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        IsNodeCacheEnabled.Subscribe(b => _editorConfig.IsNodeCacheEnabled = b)
            .DisposeWith(_disposables);

        NodeCacheMaxPixels = _editorConfig.GetObservable(EditorConfig.NodeCacheMaxPixelsProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        NodeCacheMaxPixels.Subscribe(b => _editorConfig.NodeCacheMaxPixels = b)
            .DisposeWith(_disposables);

        NodeCacheMinPixels = _editorConfig.GetObservable(EditorConfig.NodeCacheMinPixelsProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        NodeCacheMinPixels.Subscribe(b => _editorConfig.NodeCacheMinPixels = b)
            .DisposeWith(_disposables);

        EnablePointerLockInProperty = _editorConfig.GetObservable(EditorConfig.EnablePointerLockInPropertyProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        EnablePointerLockInProperty.Subscribe(b => _editorConfig.EnablePointerLockInProperty = b)
            .DisposeWith(_disposables);

        SwapTimelineScrollDirection = _editorConfig.GetObservable(EditorConfig.SwapTimelineScrollDirectionProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        SwapTimelineScrollDirection.Subscribe(b => _editorConfig.SwapTimelineScrollDirection = b)
            .DisposeWith(_disposables);

        ClampResizeToOriginalLength = _editorConfig.GetObservable(EditorConfig.ClampResizeToOriginalLengthProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        ClampResizeToOriginalLength.Subscribe(b => _editorConfig.ClampResizeToOriginalLength = b)
            .DisposeWith(_disposables);

        // GPU selection
        InitializeGpuSelection();
    }

    private void InitializeGpuSelection()
    {
        var availableGpus = GraphicsContextFactory.GetAvailableDevices();

        // Build GPU list with "Auto" as first item
        var gpuItems = new List<GpuItem> { new(null, SettingsPage.SelectedGpu_Auto) };
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

    public ReactiveProperty<bool> EnableAutoSave { get; }

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

    public IReadOnlyList<GpuItem> AvailableGpus { get; private set; } = [];

    public ReactiveProperty<int> SelectedGpuIndex { get; private set; } = null!;

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

public record GpuItem(string? Name, string DisplayName);

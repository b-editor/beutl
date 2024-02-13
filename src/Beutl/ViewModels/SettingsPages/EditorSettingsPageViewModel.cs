using Beutl.Configuration;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class EditorSettingsPageViewModel
{
    private readonly ViewConfig _viewConfig;
    private readonly EditorConfig _editorConfig;

    public EditorSettingsPageViewModel()
    {
        _viewConfig = GlobalConfiguration.Instance.ViewConfig;
        _editorConfig = GlobalConfiguration.Instance.EditorConfig;

        AutoAdjustSceneDuration = _editorConfig.GetObservable(EditorConfig.AutoAdjustSceneDurationProperty).ToReactiveProperty();
        AutoAdjustSceneDuration.Subscribe(b => _editorConfig.AutoAdjustSceneDuration = b);

        EnableAutoSave = _editorConfig.GetObservable(EditorConfig.IsAutoSaveEnabledProperty).ToReactiveProperty();
        EnableAutoSave.Subscribe(b => _editorConfig.IsAutoSaveEnabled = b);

        ShowExactBoundaries = _viewConfig.GetObservable(ViewConfig.ShowExactBoundariesProperty).ToReactiveProperty();
        ShowExactBoundaries.Subscribe(b => _viewConfig.ShowExactBoundaries = b);

        IsFrameCacheEnabled = _editorConfig.GetObservable(EditorConfig.IsFrameCacheEnabledProperty).ToReactiveProperty();
        IsFrameCacheEnabled.Subscribe(b => _editorConfig.IsFrameCacheEnabled = b);

        FrameCacheMaxSize = _editorConfig.GetObservable(EditorConfig.FrameCacheMaxSizeProperty).ToReactiveProperty();
        FrameCacheMaxSize.Subscribe(b => _editorConfig.FrameCacheMaxSize = b);

        FrameCacheScale = _editorConfig.GetObservable(EditorConfig.FrameCacheScaleProperty).Select(v => (int)v).ToReactiveProperty();
        FrameCacheScale.Subscribe(b => _editorConfig.FrameCacheScale = (FrameCacheConfigScale)b);

        FrameCacheColorType = _editorConfig.GetObservable(EditorConfig.FrameCacheColorTypeProperty).Select(v => (int)v).ToReactiveProperty();
        FrameCacheColorType.Subscribe(b => _editorConfig.FrameCacheColorType = (FrameCacheConfigColorType)b);

        IsNodeCacheEnabled = _editorConfig.GetObservable(EditorConfig.IsNodeCacheEnabledProperty).ToReactiveProperty();
        IsNodeCacheEnabled.Subscribe(b => _editorConfig.IsNodeCacheEnabled = b);

        NodeCacheMaxPixels = _editorConfig.GetObservable(EditorConfig.NodeCacheMaxPixelsProperty).ToReactiveProperty();
        NodeCacheMaxPixels.Subscribe(b => _editorConfig.NodeCacheMaxPixels = b);

        NodeCacheMinPixels = _editorConfig.GetObservable(EditorConfig.NodeCacheMinPixelsProperty).ToReactiveProperty();
        NodeCacheMinPixels.Subscribe(b => _editorConfig.NodeCacheMinPixels = b);

        EnablePointerLockInProperty = _editorConfig.GetObservable(EditorConfig.EnablePointerLockInPropertyProperty).ToReactiveProperty();
        EnablePointerLockInProperty.Subscribe(b => _editorConfig.EnablePointerLockInProperty = b);

        HidePrimaryProperties = _viewConfig.GetObservable(ViewConfig.HidePrimaryPropertiesProperty).ToReactiveProperty();
        HidePrimaryProperties.Subscribe(b => _viewConfig.HidePrimaryProperties = b);

        PrimaryProperties = _viewConfig.PrimaryProperties;

        RemovePrimaryProperty.Subscribe(v => PrimaryProperties.Remove(v));
        ResetPrimaryProperty.Subscribe(_ => _viewConfig.ResetPrimaryProperties());
    }

    public ReactiveProperty<bool> AutoAdjustSceneDuration { get; }

    public ReactiveProperty<bool> EnableAutoSave { get; }

    public ReactiveProperty<bool> ShowExactBoundaries { get; }

    public ReactiveProperty<bool> IsFrameCacheEnabled { get; }

    public ReactiveProperty<double> FrameCacheMaxSize { get; }

    public ReactiveProperty<int> FrameCacheScale { get; }

    public ReactiveProperty<int> FrameCacheColorType { get; }

    public ReactiveProperty<bool> EnablePointerLockInProperty { get; }

    public ReactiveProperty<bool> HidePrimaryProperties { get; }

    public CoreList<string> PrimaryProperties { get; }

    public ReactiveCommand<string> RemovePrimaryProperty { get; } = new();

    public ReactiveCommand ResetPrimaryProperty { get; } = new();

    public ReactiveProperty<bool> IsNodeCacheEnabled { get; }

    public ReactiveProperty<int> NodeCacheMaxPixels { get; }

    public ReactiveProperty<int> NodeCacheMinPixels { get; }
}

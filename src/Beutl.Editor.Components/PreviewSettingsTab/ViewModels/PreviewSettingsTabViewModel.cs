using System.Text.Json.Nodes;

using Beutl.Configuration;

using Reactive.Bindings;

namespace Beutl.Editor.Components.PreviewSettingsTab.ViewModels;

public sealed class PreviewSettingsTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private IEditorContext _editorContext;

    public PreviewSettingsTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;

        // Onion skin (migrated from PlayerViewModel's settings popup).
        IsOnionSkinEnabled = editorConfig.GetObservable(EditorConfig.IsOnionSkinEnabledProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        IsOnionSkinEnabled.Subscribe(v => editorConfig.IsOnionSkinEnabled = v).DisposeWith(_disposables);

        // NumericUpDown / Slider expose decimal? and double, so the UI-bound ReactiveProperty
        // types are widened here and cast back to int / float at the EditorConfig boundary.
        OnionSkinPrevCount = editorConfig.GetObservable(EditorConfig.OnionSkinPrevCountProperty)
            .Select(v => (decimal)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        OnionSkinPrevCount.Subscribe(v => editorConfig.OnionSkinPrevCount = (int)v).DisposeWith(_disposables);

        OnionSkinNextCount = editorConfig.GetObservable(EditorConfig.OnionSkinNextCountProperty)
            .Select(v => (decimal)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        OnionSkinNextCount.Subscribe(v => editorConfig.OnionSkinNextCount = (int)v).DisposeWith(_disposables);

        OnionSkinPrevOpacity = editorConfig.GetObservable(EditorConfig.OnionSkinPrevOpacityProperty)
            .Select(v => (double)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        OnionSkinPrevOpacity.Subscribe(v => editorConfig.OnionSkinPrevOpacity = (float)v).DisposeWith(_disposables);

        OnionSkinNextOpacity = editorConfig.GetObservable(EditorConfig.OnionSkinNextOpacityProperty)
            .Select(v => (double)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        OnionSkinNextOpacity.Subscribe(v => editorConfig.OnionSkinNextOpacity = (float)v).DisposeWith(_disposables);

        // Frame cache (mirrors EditorSettingsPageViewModel; the source of truth stays EditorConfig,
        // and EditViewModel reacts to these changes to refresh FrameCacheManager / Renderer.CacheOptions).
        IsFrameCacheEnabled = editorConfig.GetObservable(EditorConfig.IsFrameCacheEnabledProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        IsFrameCacheEnabled.Subscribe(v => editorConfig.IsFrameCacheEnabled = v).DisposeWith(_disposables);

        FrameCacheMaxSize = editorConfig.GetObservable(EditorConfig.FrameCacheMaxSizeProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        FrameCacheMaxSize.Subscribe(v => editorConfig.FrameCacheMaxSize = v).DisposeWith(_disposables);

        FrameCacheScale = editorConfig.GetObservable(EditorConfig.FrameCacheScaleProperty)
            .Select(v => (int)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        FrameCacheScale.Subscribe(v => editorConfig.FrameCacheScale = (FrameCacheConfigScale)v).DisposeWith(_disposables);

        FrameCacheColorType = editorConfig.GetObservable(EditorConfig.FrameCacheColorTypeProperty)
            .Select(v => (int)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        FrameCacheColorType.Subscribe(v => editorConfig.FrameCacheColorType = (FrameCacheConfigColorType)v).DisposeWith(_disposables);

        // Node (render) cache.
        IsNodeCacheEnabled = editorConfig.GetObservable(EditorConfig.IsNodeCacheEnabledProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        IsNodeCacheEnabled.Subscribe(v => editorConfig.IsNodeCacheEnabled = v).DisposeWith(_disposables);

        NodeCacheMaxPixels = editorConfig.GetObservable(EditorConfig.NodeCacheMaxPixelsProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        NodeCacheMaxPixels.Subscribe(v => editorConfig.NodeCacheMaxPixels = v).DisposeWith(_disposables);

        NodeCacheMinPixels = editorConfig.GetObservable(EditorConfig.NodeCacheMinPixelsProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        NodeCacheMinPixels.Subscribe(v => editorConfig.NodeCacheMinPixels = v).DisposeWith(_disposables);
    }

    public ReactiveProperty<bool> IsOnionSkinEnabled { get; }

    public ReactiveProperty<decimal> OnionSkinPrevCount { get; }

    public ReactiveProperty<decimal> OnionSkinNextCount { get; }

    public ReactiveProperty<double> OnionSkinPrevOpacity { get; }

    public ReactiveProperty<double> OnionSkinNextOpacity { get; }

    public ReactiveProperty<bool> IsFrameCacheEnabled { get; }

    public ReactiveProperty<double> FrameCacheMaxSize { get; }

    public ReactiveProperty<int> FrameCacheScale { get; }

    public ReactiveProperty<int> FrameCacheColorType { get; }

    public ReactiveProperty<bool> IsNodeCacheEnabled { get; }

    public ReactiveProperty<int> NodeCacheMaxPixels { get; }

    public ReactiveProperty<int> NodeCacheMinPixels { get; }

    public ToolTabExtension Extension => PreviewSettingsTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string Header => Strings.PreviewSettings;

    public void Dispose()
    {
        _disposables.Dispose();
        _editorContext = null!;
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }
}

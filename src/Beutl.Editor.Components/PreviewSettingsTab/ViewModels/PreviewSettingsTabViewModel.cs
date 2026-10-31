using System.Text.Json.Nodes;

using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Models;
using Beutl.PropertyAdapters;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.Editor.Components.PreviewSettingsTab.ViewModels;

public sealed class PreviewSettingsTabViewModel : IToolContext, IPropertyEditorContextVisitor
{
    private readonly CompositeDisposable _disposables = [];
    private readonly HistoryManager _history;
    private IEditorContext _editorContext;

    public PreviewSettingsTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        var factory = editorContext.GetRequiredService<IPropertyEditorFactory>();
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;

        var quality = editorContext.GetService<IPreviewRenderQuality>();
        IsRenderQualityAvailable = quality is not null;
        PreviewScale = quality?.PreviewScale;
        PreviewScaleOptions = quality?.PreviewScaleOptions;
        // The render quality and the node cache are disabled during playback, which holds the render thread.
        IsPlaying = editorContext.GetService<IPreviewPlayer>()?.IsPlaying
                    ?? new ReactivePropertySlim<bool>(false).DisposeWith(_disposables);

        // Editors target global EditorConfig, not the scene, so commit into a dedicated
        // HistoryManager (same as the Output / encoder-settings editors).
        _history = new HistoryManager(new OperationSequenceGenerator());

        // Each group's "enabled" ToggleSwitch (bound two-way below) gates its dependent rows,
        // which reuse the standard property editors. EditorConfig stays the single source of truth.
        IsOnionSkinEnabled = BindToggle(config.IsOnionSkinEnabled);
        AddEditors(factory, OnionSkinProperties,
            new EnginePropertyAdapter<int>(config.OnionSkinPrevCount, config),
            new EnginePropertyAdapter<int>(config.OnionSkinNextCount, config),
            new EnginePropertyAdapter<float>(config.OnionSkinPrevOpacity, config),
            new EnginePropertyAdapter<float>(config.OnionSkinNextOpacity, config));

        Guides = new PreviewGuideSettingsViewModel(config).DisposeWith(_disposables);

        IsFrameCacheEnabled = BindToggle(config.IsFrameCacheEnabled);
        AddEditors(factory, FrameCacheProperties,
            new EnginePropertyAdapter<double>(config.FrameCacheMaxSize, config),
            new EnginePropertyAdapter<FrameCacheConfigScale>(config.FrameCacheScale, config),
            new EnginePropertyAdapter<FrameCacheConfigColorType>(config.FrameCacheColorType, config));

        IsNodeCacheEnabled = BindToggle(config.IsNodeCacheEnabled);
        AddEditors(factory, NodeCacheProperties,
            new EnginePropertyAdapter<int>(config.NodeCacheMaxPixels, config),
            new EnginePropertyAdapter<int>(config.NodeCacheMinPixels, config));

        // int-backed so ComboBox.SelectedIndex (an int) round-trips: binding SelectedIndex directly to an
        // enum-typed ReactiveProperty writes the index back untranslated and the setting never updates.
        PreviewSourceMode = config.PreviewSourceMode.GetObservable()
            .Select(v => (int)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        PreviewSourceMode.Subscribe(config.SetPreviewSourceModeFromIndex)
            .DisposeWith(_disposables);
    }

    public bool IsRenderQualityAvailable { get; }

    public ReactiveProperty<int> PreviewSourceMode { get; }

    public IReactiveProperty<RenderScale>? PreviewScale { get; }

    public IReadOnlyList<RenderScale>? PreviewScaleOptions { get; }

    public IReadOnlyReactiveProperty<bool> IsPlaying { get; }

    public ReactiveProperty<bool> IsOnionSkinEnabled { get; }

    public CoreList<IPropertyEditorContext?> OnionSkinProperties { get; } = [];

    public PreviewGuideSettingsViewModel Guides { get; }

    public ReactiveProperty<bool> IsFrameCacheEnabled { get; }

    public CoreList<IPropertyEditorContext?> FrameCacheProperties { get; } = [];

    public ReactiveProperty<bool> IsNodeCacheEnabled { get; }

    public CoreList<IPropertyEditorContext?> NodeCacheProperties { get; } = [];

    public ToolTabExtension Extension => PreviewSettingsTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.PreviewSettings);

    private ReactiveProperty<bool> BindToggle(IProperty<bool> property)
        => SettingBinding.Bind(property, v => v, v => v, _disposables);

    private void AddEditors(
        IPropertyEditorFactory factory, CoreList<IPropertyEditorContext?> destination,
        params IPropertyAdapter[] adapters)
    {
        foreach (IPropertyAdapter adapter in adapters)
        {
            IPropertyEditorContext? context = factory.CreateEditor(adapter);
            // Wire the editor's service provider so Commit() resolves our HistoryManager.
            context?.Accept(this);
            destination.Add(context);
        }
    }

    public void Visit(IPropertyEditorContext context)
    {
    }

    public void Dispose()
    {
        foreach (IPropertyEditorContext? context in OnionSkinProperties
                     .Concat(FrameCacheProperties)
                     .Concat(NodeCacheProperties))
        {
            context?.Dispose();
        }

        OnionSkinProperties.Clear();
        FrameCacheProperties.Clear();
        NodeCacheProperties.Clear();
        _disposables.Dispose();
        _history.Dispose();
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
        // Route editor Commit() into our EditorConfig-rooted history, not the scene's.
        if (serviceType == typeof(HistoryManager))
        {
            return _history;
        }

        return _editorContext.GetService(serviceType);
    }
}

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
        // Disabled during playback to avoid mid-play renderer rebuilds.
        IsPlaying = editorContext.GetService<IPreviewPlayer>()?.IsPlaying
                    ?? new ReactivePropertySlim<bool>(false).DisposeWith(_disposables);

        // Editors target global EditorConfig, not the scene, so commit into a dedicated
        // EditorConfig-rooted HistoryManager (same as the Output / encoder-settings editors).
        _history = new HistoryManager(config, new OperationSequenceGenerator());

        // Each group's "enabled" ToggleSwitch (bound two-way below) gates its dependent rows,
        // which reuse the standard property editors. EditorConfig stays the single source of truth.
        IsOnionSkinEnabled = BindToggle(config, EditorConfig.IsOnionSkinEnabledProperty, v => config.IsOnionSkinEnabled = v);
        AddEditors(factory, OnionSkinProperties,
            new CorePropertyAdapter<int>(EditorConfig.OnionSkinPrevCountProperty, config),
            new CorePropertyAdapter<int>(EditorConfig.OnionSkinNextCountProperty, config),
            new CorePropertyAdapter<float>(EditorConfig.OnionSkinPrevOpacityProperty, config),
            new CorePropertyAdapter<float>(EditorConfig.OnionSkinNextOpacityProperty, config));

        IsFrameCacheEnabled = BindToggle(config, EditorConfig.IsFrameCacheEnabledProperty, v => config.IsFrameCacheEnabled = v);
        AddEditors(factory, FrameCacheProperties,
            new CorePropertyAdapter<double>(EditorConfig.FrameCacheMaxSizeProperty, config),
            new CorePropertyAdapter<FrameCacheConfigScale>(EditorConfig.FrameCacheScaleProperty, config),
            new CorePropertyAdapter<FrameCacheConfigColorType>(EditorConfig.FrameCacheColorTypeProperty, config));

        IsNodeCacheEnabled = BindToggle(config, EditorConfig.IsNodeCacheEnabledProperty, v => config.IsNodeCacheEnabled = v);
        AddEditors(factory, NodeCacheProperties,
            new CorePropertyAdapter<int>(EditorConfig.NodeCacheMaxPixelsProperty, config),
            new CorePropertyAdapter<int>(EditorConfig.NodeCacheMinPixelsProperty, config));

        // int-backed so ComboBox.SelectedIndex (an int) round-trips: binding SelectedIndex directly to an
        // enum-typed ReactiveProperty writes the index back untranslated and the setting never updates.
        PreviewSourceMode = config.GetObservable(EditorConfig.PreviewSourceModeProperty)
            .Select(v => (int)v)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        PreviewSourceMode.Subscribe(v => config.PreviewSourceMode = (PreviewSourceMode)v)
            .DisposeWith(_disposables);
    }

    public bool IsRenderQualityAvailable { get; }

    public ReactiveProperty<int> PreviewSourceMode { get; }

    public IReactiveProperty<RenderScale>? PreviewScale { get; }

    public IReadOnlyList<RenderScale>? PreviewScaleOptions { get; }

    public IReadOnlyReactiveProperty<bool> IsPlaying { get; }

    public ReactiveProperty<bool> IsOnionSkinEnabled { get; }

    public CoreList<IPropertyEditorContext?> OnionSkinProperties { get; } = [];

    public ReactiveProperty<bool> IsFrameCacheEnabled { get; }

    public CoreList<IPropertyEditorContext?> FrameCacheProperties { get; } = [];

    public ReactiveProperty<bool> IsNodeCacheEnabled { get; }

    public CoreList<IPropertyEditorContext?> NodeCacheProperties { get; } = [];

    public ToolTabExtension Extension => PreviewSettingsTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string Header => Strings.PreviewSettings;

    private ReactiveProperty<bool> BindToggle(EditorConfig config, CoreProperty<bool> property, Action<bool> setter)
    {
        ReactiveProperty<bool> reactive = config.GetObservable(property)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        reactive.Subscribe(setter).DisposeWith(_disposables);
        return reactive;
    }

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

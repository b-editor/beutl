using System.Text.Json.Nodes;

using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.PropertyAdapters;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.Editor.Components.PreviewSettingsTab.ViewModels;

public sealed class PreviewSettingsTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private IEditorContext _editorContext;

    public PreviewSettingsTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        var factory = editorContext.GetRequiredService<IPropertyEditorFactory>();
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;

        // Each group's "enabled" flag is shown as a ToggleSwitch next to the section header
        // (bound two-way below), and also drives the IsEnabled of the dependent rows.
        // The dependent rows themselves go through the property-editor mechanism so they reuse
        // the standard editor for their CoreProperty type (number / enum). EditorConfig stays
        // the single source of truth, which the rest of the app already reacts to.
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
    }

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

    private static void AddEditors(
        IPropertyEditorFactory factory, CoreList<IPropertyEditorContext?> destination,
        params IPropertyAdapter[] adapters)
    {
        foreach (IPropertyAdapter adapter in adapters)
        {
            destination.Add(factory.CreateEditor(adapter));
        }
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
        return _editorContext.GetService(serviceType);
    }
}

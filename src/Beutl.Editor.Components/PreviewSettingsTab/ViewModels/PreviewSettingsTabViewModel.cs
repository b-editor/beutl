using System.Text.Json.Nodes;

using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.PropertyAdapters;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.Editor.Components.PreviewSettingsTab.ViewModels;

public sealed class PreviewSettingsTabViewModel : IToolContext
{
    private IEditorContext _editorContext;

    public PreviewSettingsTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        var factory = editorContext.GetRequiredService<IPropertyEditorFactory>();
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;

        // Build the editors through the property-editor mechanism so each row reuses the
        // standard editor for its CoreProperty type (toggle / number / enum). The settings
        // themselves stay on EditorConfig, which the rest of the app already reacts to.
        AddEditors(factory, OnionSkinProperties,
            new CorePropertyAdapter<bool>(EditorConfig.IsOnionSkinEnabledProperty, config),
            new CorePropertyAdapter<int>(EditorConfig.OnionSkinPrevCountProperty, config),
            new CorePropertyAdapter<int>(EditorConfig.OnionSkinNextCountProperty, config),
            new CorePropertyAdapter<float>(EditorConfig.OnionSkinPrevOpacityProperty, config),
            new CorePropertyAdapter<float>(EditorConfig.OnionSkinNextOpacityProperty, config));

        AddEditors(factory, FrameCacheProperties,
            new CorePropertyAdapter<bool>(EditorConfig.IsFrameCacheEnabledProperty, config),
            new CorePropertyAdapter<double>(EditorConfig.FrameCacheMaxSizeProperty, config),
            new CorePropertyAdapter<FrameCacheConfigScale>(EditorConfig.FrameCacheScaleProperty, config),
            new CorePropertyAdapter<FrameCacheConfigColorType>(EditorConfig.FrameCacheColorTypeProperty, config));

        AddEditors(factory, NodeCacheProperties,
            new CorePropertyAdapter<bool>(EditorConfig.IsNodeCacheEnabledProperty, config),
            new CorePropertyAdapter<int>(EditorConfig.NodeCacheMaxPixelsProperty, config),
            new CorePropertyAdapter<int>(EditorConfig.NodeCacheMinPixelsProperty, config));
    }

    public CoreList<IPropertyEditorContext?> OnionSkinProperties { get; } = [];

    public CoreList<IPropertyEditorContext?> FrameCacheProperties { get; } = [];

    public CoreList<IPropertyEditorContext?> NodeCacheProperties { get; } = [];

    public ToolTabExtension Extension => PreviewSettingsTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string Header => Strings.PreviewSettings;

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

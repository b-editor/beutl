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

        // Build the editors through the property-editor mechanism so each row reuses the
        // standard editor for its CoreProperty type (toggle / number / enum). The settings
        // themselves stay on EditorConfig, which the rest of the app already reacts to.
        // The "enabled" toggle stays interactive while its dependent rows are disabled
        // (IsEnabled bound to the flag) whenever the group is turned off.
        IsOnionSkinEnabled = config.GetObservable(EditorConfig.IsOnionSkinEnabledProperty)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        OnionSkinEnabledEditor = factory.CreateEditor(
            new CorePropertyAdapter<bool>(EditorConfig.IsOnionSkinEnabledProperty, config));
        AddEditors(factory, OnionSkinProperties,
            new CorePropertyAdapter<int>(EditorConfig.OnionSkinPrevCountProperty, config),
            new CorePropertyAdapter<int>(EditorConfig.OnionSkinNextCountProperty, config),
            new CorePropertyAdapter<float>(EditorConfig.OnionSkinPrevOpacityProperty, config),
            new CorePropertyAdapter<float>(EditorConfig.OnionSkinNextOpacityProperty, config));

        IsFrameCacheEnabled = config.GetObservable(EditorConfig.IsFrameCacheEnabledProperty)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        FrameCacheEnabledEditor = factory.CreateEditor(
            new CorePropertyAdapter<bool>(EditorConfig.IsFrameCacheEnabledProperty, config));
        AddEditors(factory, FrameCacheProperties,
            new CorePropertyAdapter<double>(EditorConfig.FrameCacheMaxSizeProperty, config),
            new CorePropertyAdapter<FrameCacheConfigScale>(EditorConfig.FrameCacheScaleProperty, config),
            new CorePropertyAdapter<FrameCacheConfigColorType>(EditorConfig.FrameCacheColorTypeProperty, config));

        IsNodeCacheEnabled = config.GetObservable(EditorConfig.IsNodeCacheEnabledProperty)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        NodeCacheEnabledEditor = factory.CreateEditor(
            new CorePropertyAdapter<bool>(EditorConfig.IsNodeCacheEnabledProperty, config));
        AddEditors(factory, NodeCacheProperties,
            new CorePropertyAdapter<int>(EditorConfig.NodeCacheMaxPixelsProperty, config),
            new CorePropertyAdapter<int>(EditorConfig.NodeCacheMinPixelsProperty, config));
    }

    public IPropertyEditorContext? OnionSkinEnabledEditor { get; }

    public CoreList<IPropertyEditorContext?> OnionSkinProperties { get; } = [];

    public ReadOnlyReactivePropertySlim<bool> IsOnionSkinEnabled { get; }

    public IPropertyEditorContext? FrameCacheEnabledEditor { get; }

    public CoreList<IPropertyEditorContext?> FrameCacheProperties { get; } = [];

    public ReadOnlyReactivePropertySlim<bool> IsFrameCacheEnabled { get; }

    public IPropertyEditorContext? NodeCacheEnabledEditor { get; }

    public CoreList<IPropertyEditorContext?> NodeCacheProperties { get; } = [];

    public ReadOnlyReactivePropertySlim<bool> IsNodeCacheEnabled { get; }

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
        IPropertyEditorContext?[] enabledEditors =
            [OnionSkinEnabledEditor, FrameCacheEnabledEditor, NodeCacheEnabledEditor];
        foreach (IPropertyEditorContext? context in enabledEditors
                     .Concat(OnionSkinProperties)
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

using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeTreeInputTab.ViewModels;

public sealed class NodeTreeInputTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private IEditorContext _editorContext;

    public NodeTreeInputTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        Element = editorContext.GetRequiredService<IEditorSelection>().SelectedObject
            .Select(x => x as Element)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Element.CombineWithPrevious()
            .Subscribe(obj =>
            {
                InnerViewModel.Value?.Dispose();
                InnerViewModel.Value = null;
                if (obj.NewValue is Element newValue)
                {
                    InnerViewModel.Value = new NodeTreeInputViewModel(newValue, this);
                }
            })
            .DisposeWith(_disposables);
    }

    public string Header => Strings.NodeTree;

    public ToolTabExtension Extension => NodeTreeInputTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.RightUpperBottom);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public ReactiveProperty<Element?> Element { get; }

    [Obsolete("Use Element property instead.")]
    public ReactiveProperty<Element?> Layer => Element;

    public ReactivePropertySlim<NodeTreeInputViewModel?> InnerViewModel { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();

        InnerViewModel.Value?.Dispose();
        InnerViewModel.Value = null;
        Element.Value = null;
        _editorContext = null!;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Element))
            return Element.Value;

        return _editorContext.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {

    }

    public void WriteToJson(JsonObject json)
    {

    }
}

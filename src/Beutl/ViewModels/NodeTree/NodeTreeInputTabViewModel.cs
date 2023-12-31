using System.Text.Json.Nodes;

using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public sealed class NodeTreeInputTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private EditViewModel _editViewModel;

    public NodeTreeInputTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;
        Element = editViewModel.SelectedObject
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

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

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
        _editViewModel = null!;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Element))
            return Element.Value;

        return _editViewModel.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {

    }

    public void WriteToJson(JsonObject json)
    {

    }
}

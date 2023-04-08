using System.Text.Json.Nodes;

using Beutl.Framework;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public sealed class NodeTreeInputTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = new();
    private EditViewModel _editViewModel;

    public NodeTreeInputTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;
        Layer = editViewModel.SelectedObject
            .Select(x => x as Layer)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Layer.CombineWithPrevious()
            .Subscribe(obj =>
            {
                InnerViewModel.Value?.Dispose();
                InnerViewModel.Value = null;
                if (obj.NewValue is Layer newValue)
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

    public ReactiveProperty<Layer?> Layer { get; }

    public ReactivePropertySlim<NodeTreeInputViewModel?> InnerViewModel { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();

        InnerViewModel.Value?.Dispose();
        InnerViewModel.Value = null;
        Layer.Value = null;
        _editViewModel = null!;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Layer))
            return Layer.Value;

        return _editViewModel.GetService(serviceType);
    }

    public void ReadFromJson(JsonNode json)
    {

    }

    public void WriteToJson(ref JsonNode json)
    {

    }
}

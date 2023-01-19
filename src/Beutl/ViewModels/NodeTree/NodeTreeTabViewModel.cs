using System.Text.Json.Nodes;

using Beutl.Framework;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public sealed class NodeTreeTabViewModel : IToolContext
{
    private readonly ReactiveProperty<bool> _isSelected = new(true);
    private readonly EditViewModel _editViewModel;
    private readonly CompositeDisposable _disposables = new();
    private IDisposable? _innerDisposable;

    public NodeTreeTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;

        Layer.Subscribe(v =>
        {
            foreach (NodeViewModel item in Nodes.GetMarshal().Value)
            {
                item.Dispose();
            }
            Nodes.Clear();
            _innerDisposable?.Dispose();
            _innerDisposable = null;

            if (v != null)
            {
                _innerDisposable = v.Space.Nodes.ForEachItem(
                    (idx, item) => Nodes.Insert(idx, new NodeViewModel(item)),
                    (idx, _) =>
                    {
                        NodeViewModel viewModel = Nodes[idx];
                        viewModel.Dispose();
                        Nodes.RemoveAt(idx);
                    },
                    () =>
                    {
                        foreach (NodeViewModel item in Nodes.GetMarshal().Value)
                        {
                            item.Dispose();
                        }
                        Nodes.Clear();
                    });
            }
        }).DisposeWith(_disposables);
    }

    public ToolTabExtension Extension => NodeTreeTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected => _isSelected;

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Bottom;

    public ReactivePropertySlim<Layer?> Layer { get; } = new();

    public CoreList<NodeViewModel> Nodes { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
        _innerDisposable?.Dispose();
        _innerDisposable = null;
    }

    public void ReadFromJson(JsonNode json)
    {
    }

    public void WriteToJson(ref JsonNode json)
    {
    }
}

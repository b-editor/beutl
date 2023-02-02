using System.Text.Json.Nodes;

using Beutl.Framework;
using Beutl.NodeTree;
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
                    (idx, item) =>
                    {
                        var viewModel = new NodeViewModel(item);
                        Nodes.Insert(idx, viewModel);
                    },
                    (idx, _) =>
                    {
                        NodeViewModel viewModel = Nodes[idx];
                        Nodes.RemoveAt(idx);
                        viewModel.Dispose();
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

    public SocketViewModel? FindSocketViewModel(ISocket socket)
    {
        foreach (NodeViewModel node in Nodes.GetMarshal().Value)
        {
            foreach (NodeItemViewModel item in node.Items.GetMarshal().Value)
            {
                if (item.Model == socket)
                {
                    return item as SocketViewModel;
                }
            }
        }

        return null;
    }

    public void Dispose()
    {
        foreach (NodeViewModel item in Nodes)
        {
            item.Dispose();
        }
        Nodes.Clear();

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

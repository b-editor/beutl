using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Collections;
using Beutl.Controls;
using Beutl.Editor.Services;
using Beutl.NodeGraph;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

public class NodePortViewModel : NodeMemberViewModel
{
    private readonly IEditorContext _editorContext;
    private readonly CompositeDisposable _disposables = new();
    private IDisposable? _connectionsSubscription;

    public NodePortViewModel(INodePort? port, IPropertyEditorContext? propertyEditorContext, GraphNodeViewModel nodeViewModel)
        : base(port, propertyEditorContext, nodeViewModel)
    {
        if (port != null)
        {
            Color = new ImmutableSolidColorBrush(port.Color.ToAvaColor());
        }
        else
        {
            Color = Brushes.Gray;
        }

        SubscribeModelConnections();
        _connectionsSubscription = Connections.ForEachItem(
            (_, _) => IsConnected.Value = Connections.Count > 0,
            (_, _) => IsConnected.Value = Connections.Count > 0,
            () => IsConnected.Value = false);

        _editorContext = nodeViewModel.EditorContext;
    }

    public new INodePort? Model => base.Model as INodePort;

    public ReactivePropertySlim<bool> IsConnected { get; } = new();

    public IBrush Color { get; }

    public CoreList<ConnectionViewModel> Connections { get; } = [];

    private void SetViewModel(ConnectionViewModel viewModel)
    {
        // 終わってるコード❤
        // 派生クラスで実装すべき
        if (Model is IInputPort)
        {
            viewModel.InputPortVM.Value = this as InputPortViewModel;
        }
        else if (Model is IOutputPort)
        {
            viewModel.OutputPortVM.Value = this as OutputPortViewModel;
        }
    }

    private void SubscribeModelConnections()
    {
        var connections = Model switch
        {
            IOutputPort outputNodePort => outputNodePort.Connections,
            IListPort outputNodePort => outputNodePort.Connections,
            _ => null
        };

        if (connections != null)
        {
            connections.ForEachItem(
                    connection =>
                    {
                        var graph = GraphNodeViewModel.NodeGraphViewModel;
                        // すでに存在する場合はスキップする
                        var connVM = graph.AllConnections.FirstOrDefault(i => i.Connection.Id == connection.Id);
                        if (connVM == null)
                        {
                            if (connection.Value == null) return;

                            connVM = new ConnectionViewModel(graph, connection.Value);
                            graph.AllConnections.Add(connVM);
                        }

                        SetViewModel(connVM);
                        if (!Connections.Contains(connVM))
                        {
                            Connections.Insert(GetInsertionIndex(connection.Id), connVM);
                        }
                    },
                    connection =>
                    {
                        var connVM = Connections.FirstOrDefault(c => c.Connection.Id == connection.Id);
                        if (connVM != null)
                        {
                            Connections.Remove(connVM);
                            // NodeGraphViewModel側でDisposeされるのでDisposeしない
                        }
                    },
                    () => Connections.Clear())
                .DisposeWith(_disposables);
        }
        else if (Model is IInputPort inputNodePort)
        {
            inputNodePort.GetConnectionObservable()
                .Subscribe(connection =>
                {
                    if (connection.IsNull)
                    {
                        Connections.Clear();
                    }
                    else
                    {
                        var graph = GraphNodeViewModel.NodeGraphViewModel;
                        var connVM = graph.AllConnections.FirstOrDefault(i => i.Connection.Id == connection.Id);
                        if (connVM == null)
                        {
                            if (connection.Value == null)
                            {
                                return;
                            }

                            connVM = new ConnectionViewModel(graph, connection.Value);
                            graph.AllConnections.Add(connVM);
                        }

                        SetViewModel(connVM);
                        Connections.Add(connVM);
                    }
                })
                .DisposeWith(_disposables);
        }
    }

    public int GetInsertionIndex(Guid id)
    {
        var connections = Model switch
        {
            IOutputPort outputNodePort => outputNodePort.Connections,
            IListPort outputNodePort => outputNodePort.Connections,
            _ => null
        };
        if (connections == null)
            return Connections.Count;

        int targetOrder = connections.Index().FirstOrDefault(i => i.Item.Id == id, (-1, null)).Index;
        if (targetOrder < 0)
            return Connections.Count;

        // 元の順序で、自分より後にあるべき接続の前に挿入
        for (int i = 0; i < Connections.Count; i++)
        {
            int existingOrder = connections.IndexOf(Connections[i].Connection);
            if (existingOrder < 0 || existingOrder > targetOrder)
                return i;
        }

        return Connections.Count;
    }

    public bool TryConnect(NodePortViewModel target)
    {
        GraphModel graph = GraphNodeViewModel.NodeGraphViewModel.NodeGraph;
        INodeGraphMutationService service = _editorContext.GetRequiredService<INodeGraphMutationService>();

        NodeConnectOutcome outcome = service.TryConnect(graph, GraphNode, Model, target.GraphNode, target.Model);
        return outcome == NodeConnectOutcome.Connected;
    }

    public bool TryDisconnect(NodePortViewModel target, ConnectionViewModel? connection)
    {
        if (Model == null || target.Model == null) return false;

        GraphModel graph = GraphNodeViewModel.NodeGraphViewModel.NodeGraph;
        return _editorContext.GetRequiredService<INodeGraphMutationService>()
            .TryDisconnect(graph, Model, target.Model, connection?.Connection);
    }

    public void DisconnectAll()
    {
        if (Model == null) return;

        GraphModel graph = GraphNodeViewModel.NodeGraphViewModel.NodeGraph;
        _editorContext.GetRequiredService<INodeGraphMutationService>()
            .DisconnectAll(graph, Model);
    }

    public void Remove()
    {
        if (Model == null) return;

        _editorContext.GetRequiredService<INodeGraphMutationService>()
            .RemovePort(GraphNode, Model);
    }

    public void MoveConnectionSlot(int oldIndex, int newIndex)
    {
        if (Model == null) return;

        _editorContext.GetRequiredService<INodeGraphMutationService>()
            .MoveConnectionSlot(Model, oldIndex, newIndex);
    }

    public void DisconnectConnection(ConnectionViewModel connVM)
    {
        GraphModel graph = GraphNodeViewModel.NodeGraphViewModel.NodeGraph;
        _editorContext.GetRequiredService<INodeGraphMutationService>()
            .DisconnectConnection(graph, connVM.Connection);
    }

    public void UpdateName(string? e)
    {
        if (Model == null || e == null) return;

        _editorContext.GetRequiredService<INodeGraphMutationService>()
            .RenamePort(Model, e);
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _connectionsSubscription?.Dispose();
        Connections.Clear();
    }
}

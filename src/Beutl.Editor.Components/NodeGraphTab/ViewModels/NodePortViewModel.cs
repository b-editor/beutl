using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Collections;
using Beutl.Controls;
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

    private static bool SortNodePort(
        INodePort first, INodePort second,
        [NotNullWhen(true)] out IInputPort? inputNodePort,
        [NotNullWhen(true)] out IOutputPort? outputNodePort)
    {
        if (first is IInputPort input)
        {
            inputNodePort = input;
            outputNodePort = second as IOutputPort;
        }
        else
        {
            inputNodePort = second as IInputPort;
            outputNodePort = first as IOutputPort;
        }

        return outputNodePort != null && inputNodePort != null;
    }

    public bool TryConnect(NodePortViewModel target)
    {
        HistoryManager history = _editorContext.GetRequiredService<HistoryManager>();
        if (target.Model == null ^ Model == null)
        {
            // どちらかがNull
            IDynamicPortNode? groupNode = null;
            INodePort? port = null;
            switch ((GraphNode, target.GraphNode))
            {
                case (IDynamicPortNode node1, _):
                    groupNode = node1;
                    port = target.Model;
                    break;

                case (_, IDynamicPortNode node2):
                    groupNode = node2;
                    port = Model;
                    break;
            }

            if (groupNode != null && port != null
                                  && groupNode.AddNodePort(port, out _))
            {
                history.Commit(CommandNames.AddPort);
            }

            return false;
        }
        else if (Model != null && target.Model != null
                               && SortNodePort(Model, target.Model, out IInputPort? inputNodePort,
                                   out IOutputPort? outputNodePort))
        {
            var graph = GraphNodeViewModel.NodeGraphViewModel.NodeGraph;
            graph.Connect(inputNodePort, outputNodePort);
            history.Commit(CommandNames.ConnectPort);

            return true;
        }
        else
        {
            return false;
        }
    }

    public bool TryDisconnect(NodePortViewModel target, ConnectionViewModel? connection)
    {
        HistoryManager history = _editorContext.GetRequiredService<HistoryManager>();
        var graph = GraphNodeViewModel.NodeGraphViewModel.NodeGraph;
        var conn = connection?.Connection;
        if (conn == null && Model != null && target.Model != null
            && SortNodePort(Model, target.Model, out IInputPort? inputNodePort,
                out IOutputPort? outputNodePort))
        {
            conn = graph.AllConnections.FirstOrDefault(c =>
                c.Input.Id == inputNodePort.Id && c.Output.Id == outputNodePort.Id);
        }

        if (conn != null)
        {
            graph.Disconnect(conn);
            history.Commit(CommandNames.DisconnectPort);

            return true;
        }
        else
        {
            return false;
        }
    }

    public void DisconnectAll()
    {
        HistoryManager history = _editorContext.GetRequiredService<HistoryManager>();
        var graph = GraphNodeViewModel.NodeGraphViewModel.NodeGraph;

        var connections = Model switch
        {
            IOutputPort outputNodePort => outputNodePort.Connections,
            IListPort outputNodePort => outputNodePort.Connections,
            _ => null
        };

        if (connections != null)
        {
            // Disconnect内でConnectionsから要素を削除するのでToArrayする必要がある
            foreach (Reference<Connection> connection in connections.ToArray())
            {
                if (connection.Value != null)
                    graph.Disconnect(connection.Value);
            }

            history.Commit(CommandNames.DisconnectPort);
        }
        else if (Model is IInputPort { Connection.Value: { } connection })
        {
            graph.Disconnect(connection);
            history.Commit(CommandNames.DisconnectPort);
        }
    }

    public void Remove()
    {
        if (Model is not IDynamicPort generatedNodePort) return;

        GraphModel? tree = GraphNode.FindHierarchicalParent<GraphModel>();
        if (tree == null) return;

        var connections = generatedNodePort switch
        {
            IOutputPort outputNodePort => outputNodePort.Connections,
            IListPort listNodePort => listNodePort.Connections,
            IInputPort { Connection: var connection } => [connection],
            _ => []
        };
        foreach (var connection in connections.ToArray())
        {
            if (connection.Value != null)
                tree.Disconnect(connection.Value);
        }

        GraphNode.Items.Remove(generatedNodePort);
        _editorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.RemovePort);
    }

    public void MoveConnectionSlot(int oldIndex, int newIndex)
    {
        if (Model is IListPort listNodePort)
        {
            listNodePort.MoveConnection(oldIndex, newIndex);
            _editorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.MoveConnection);
        }
    }

    public void DisconnectConnection(ConnectionViewModel connVM)
    {
        var graph = GraphNodeViewModel.NodeGraphViewModel.NodeGraph;
        Connection connection = connVM.Connection;
        graph.Disconnect(connection);
        _editorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.DisconnectPort);
    }

    public void UpdateName(string? e)
    {
        Model!.Name = e!;
        _editorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.RenamePort);
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _connectionsSubscription?.Dispose();
        Connections.Clear();
    }
}

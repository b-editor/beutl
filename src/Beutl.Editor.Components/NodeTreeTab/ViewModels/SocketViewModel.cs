using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Collections;
using Beutl.Controls;
using Beutl.NodeTree;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeTreeTab.ViewModels;

public class SocketViewModel : NodeItemViewModel
{
    private readonly IEditorContext _editorContext;
    private readonly CompositeDisposable _disposables = new();
    private IDisposable? _connectionsSubscription;

    public SocketViewModel(ISocket? socket, IPropertyEditorContext? propertyEditorContext, NodeViewModel nodeViewModel)
        : base(socket, propertyEditorContext, nodeViewModel)
    {
        if (socket != null)
        {
            Color = new ImmutableSolidColorBrush(socket.Color.ToAvaColor());
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

    public new ISocket? Model => base.Model as ISocket;

    public ReactivePropertySlim<bool> IsConnected { get; } = new();

    public IBrush Color { get; }

    public CoreList<ConnectionViewModel> Connections { get; } = [];

    private void SetViewModel(ConnectionViewModel viewModel)
    {
        // 終わってるコード❤
        // 派生クラスで実装すべき
        if (Model is IInputSocket)
        {
            viewModel.InputSocketVM.Value = this as InputSocketViewModel;
        }
        else if (Model is IOutputSocket)
        {
            viewModel.OutputSocketVM.Value = this as OutputSocketViewModel;
        }
    }

    private void SubscribeModelConnections()
    {
        var connections = Model switch
        {
            IOutputSocket outputSocket => outputSocket.Connections,
            IListSocket outputSocket => outputSocket.Connections,
            _ => null
        };

        if (connections != null)
        {
            connections.ForEachItem(
                    connection =>
                    {
                        var nodeTree = NodeViewModel.NodeTreeViewModel;
                        // すでに存在する場合はスキップする
                        var connVM = nodeTree.AllConnections.FirstOrDefault(i => i.Connection.Id == connection.Id);
                        if (connVM == null)
                        {
                            if (connection.Value == null) return;

                            connVM = new ConnectionViewModel(nodeTree, connection.Value);
                            nodeTree.AllConnections.Add(connVM);
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
                            // NodeTreeViewModel側でDisposeされるのでDisposeしない
                        }
                    },
                    () => Connections.Clear())
                .DisposeWith(_disposables);
        }
        else if (Model is IInputSocket inputSocket)
        {
            inputSocket.GetConnectionObservable()
                .Subscribe(connection =>
                {
                    if (connection.IsNull)
                    {
                        Connections.Clear();
                    }
                    else
                    {
                        var nodeTree = NodeViewModel.NodeTreeViewModel;
                        var connVM = nodeTree.AllConnections.FirstOrDefault(i => i.Connection.Id == connection.Id);
                        if (connVM == null)
                        {
                            if (connection.Value == null)
                            {
                                return;
                            }

                            connVM = new ConnectionViewModel(nodeTree, connection.Value);
                            nodeTree.AllConnections.Add(connVM);
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
            IOutputSocket outputSocket => outputSocket.Connections,
            IListSocket outputSocket => outputSocket.Connections,
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

    private static bool SortSocket(
        ISocket first, ISocket second,
        [NotNullWhen(true)] out IInputSocket? inputSocket,
        [NotNullWhen(true)] out IOutputSocket? outputSocket)
    {
        if (first is IInputSocket input)
        {
            inputSocket = input;
            outputSocket = second as IOutputSocket;
        }
        else
        {
            inputSocket = second as IInputSocket;
            outputSocket = first as IOutputSocket;
        }

        return outputSocket != null && inputSocket != null;
    }

    public bool TryConnect(SocketViewModel target)
    {
        HistoryManager history = _editorContext.GetRequiredService<HistoryManager>();
        if (target.Model == null ^ Model == null)
        {
            // どちらかがNull
            ISocketsCanBeAdded? groupNode = null;
            ISocket? socket = null;
            switch ((Node, target.Node))
            {
                case (ISocketsCanBeAdded node1, _):
                    groupNode = node1;
                    socket = target.Model;
                    break;

                case (_, ISocketsCanBeAdded node2):
                    groupNode = node2;
                    socket = Model;
                    break;
            }

            if (groupNode != null && socket != null
                                  && groupNode.AddSocket(socket, out _))
            {
                history.Commit(CommandNames.AddSocket);
            }

            return false;
        }
        else if (Model != null && target.Model != null
                               && SortSocket(Model, target.Model, out IInputSocket? inputSocket,
                                   out IOutputSocket? outputSocket))
        {
            var nodeTree = NodeViewModel.NodeTreeViewModel.NodeTree;
            nodeTree.Connect(inputSocket, outputSocket);
            history.Commit(CommandNames.ConnectSocket);

            return true;
        }
        else
        {
            return false;
        }
    }

    public bool TryDisconnect(SocketViewModel target, ConnectionViewModel? connection)
    {
        HistoryManager history = _editorContext.GetRequiredService<HistoryManager>();
        var nodeTree = NodeViewModel.NodeTreeViewModel.NodeTree;
        var conn = connection?.Connection;
        if (conn == null && Model != null && target.Model != null
            && SortSocket(Model, target.Model, out IInputSocket? inputSocket,
                out IOutputSocket? outputSocket))
        {
            conn = nodeTree.AllConnections.FirstOrDefault(c =>
                c.Input.Id == inputSocket.Id && c.Output.Id == outputSocket.Id);
        }

        if (conn != null)
        {
            nodeTree.Disconnect(conn);
            history.Commit(CommandNames.DisconnectSocket);

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
        var nodeTree = NodeViewModel.NodeTreeViewModel.NodeTree;

        var connections = Model switch
        {
            IOutputSocket outputSocket => outputSocket.Connections,
            IListSocket outputSocket => outputSocket.Connections,
            _ => null
        };

        if (connections != null)
        {
            // Disconnect内でConnectionsから要素を削除するのでToArrayする必要がある
            foreach (Reference<Connection> connection in connections.ToArray())
            {
                if (connection.Value != null)
                    nodeTree.Disconnect(connection.Value);
            }

            history.Commit(CommandNames.DisconnectSocket);
        }
        else if (Model is IInputSocket { Connection.Value: { } connection })
        {
            nodeTree.Disconnect(connection);
            history.Commit(CommandNames.DisconnectSocket);
        }
    }

    public void Remove()
    {
        if (Model is not IAutomaticallyGeneratedSocket generatedSocket) return;

        NodeTreeModel? tree = Node.FindHierarchicalParent<NodeTreeModel>();
        if (tree == null) return;

        var connections = generatedSocket switch
        {
            IOutputSocket outputSocket => outputSocket.Connections,
            IListSocket listSocket => listSocket.Connections,
            IInputSocket { Connection: var connection } => [connection],
            _ => []
        };
        foreach (var connection in connections.ToArray())
        {
            if (connection.Value != null)
                tree.Disconnect(connection.Value);
        }

        Node.Items.Remove(generatedSocket);
        _editorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.RemoveSocket);
    }

    public void MoveConnectionSlot(int oldIndex, int newIndex)
    {
        if (Model is IListSocket listSocket)
        {
            listSocket.MoveConnection(oldIndex, newIndex);
            _editorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.MoveConnection);
        }
    }

    public void DisconnectConnection(ConnectionViewModel connVM)
    {
        var nodeTree = NodeViewModel.NodeTreeViewModel.NodeTree;
        Connection connection = connVM.Connection;
        nodeTree.Disconnect(connection);
        _editorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.DisconnectSocket);
    }

    public void UpdateName(string? e)
    {
        Model!.Name = e!;
        _editorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.RenameSocket);
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _connectionsSubscription?.Dispose();
        Connections.Clear();
    }
}

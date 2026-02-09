using System.Text.Json.Nodes;

using Avalonia;
using Beutl.NodeTree;
using Beutl.NodeTree.Nodes.Group;

using Reactive.Bindings;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.NodeTreeTab.ViewModels;

public sealed class NodeTreeViewModel : IDisposable, IJsonSerializable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly Dictionary<NodeViewModel, CompositeDisposable> _nodeSubscriptions = [];
    private readonly IEditorContext _editorContext;

    public NodeTreeViewModel(NodeTreeModel nodeTree, IEditorContext editorContext)
    {
        NodeTree = nodeTree;
        _editorContext = editorContext;
        nodeTree.Nodes.ForEachItem(
            (idx, item) =>
            {
                var viewModel = new NodeViewModel(item, _editorContext);
                Nodes.Insert(idx, viewModel);
                SubscribeNodeConnections(viewModel);
            },
            (idx, _) =>
            {
                NodeViewModel viewModel = Nodes[idx];
                CleanUpNodeConnections(viewModel);
                UnsubscribeNodeConnections(viewModel);
                Nodes.RemoveAt(idx);
                viewModel.Dispose();
            },
            () =>
            {
                foreach (NodeViewModel item in Nodes.GetMarshal().Value)
                {
                    UnsubscribeNodeConnections(item);
                    item.Dispose();
                }
                Nodes.Clear();
                foreach (ConnectionViewModel conn in AllConnections)
                {
                    conn.Dispose();
                }
                AllConnections.Clear();
            })
            .DisposeWith(_disposables);
    }

    public CoreList<NodeViewModel> Nodes { get; } = [];

    public CoreList<ConnectionViewModel> AllConnections { get; } = [];

    public ReactiveProperty<Matrix> Matrix { get; } = new(Avalonia.Matrix.Identity);

    public NodeTreeModel NodeTree { get; }

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

    public void AddSocket(Type type, Point point)
    {
        var node = (Node)Activator.CreateInstance(type)!;
        node.Position = (point.X, point.Y);
        if (NodeTree is NodeGroup nodeGroup)
        {
            if (node is GroupInput
                && nodeGroup.Nodes.Any(x => x is GroupInput))
            {
                return;
            }
            else if (node is GroupOutput
                && nodeGroup.Nodes.Any(x => x is GroupOutput))
            {
                return;
            }
        }

        NodeTree.Nodes.Add(node);
        _editorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.AddNode);
    }

    public void Dispose()
    {
        foreach (var kvp in _nodeSubscriptions)
        {
            foreach (NodeItemViewModel item in kvp.Key.Items)
            {
                if (item is SocketViewModel { Model: ISocket socket })
                {
                    socket.Connected -= OnModelSocketConnected;
                    socket.Disconnected -= OnModelSocketDisconnected;
                }
            }
            kvp.Value.Dispose();
        }
        _nodeSubscriptions.Clear();

        foreach (ConnectionViewModel connVM in AllConnections)
        {
            connVM.Dispose();
        }
        AllConnections.Clear();

        foreach (NodeViewModel item in Nodes)
        {
            item.Dispose();
        }
        Nodes.Clear();

        _disposables.Dispose();
    }

    public void WriteToJson(JsonObject json)
    {
        var nodesJson = new JsonObject();
        foreach (NodeViewModel item in Nodes)
        {
            var nodeJson = new JsonObject();
            item.WriteToJson(nodeJson);
            nodesJson[item.Node.Id.ToString()] = nodeJson;
        }

        json[nameof(Nodes)] = nodesJson;

        Matrix m = Matrix.Value;
        json[nameof(Matrix)] = $"{m.M11},{m.M12},{m.M21},{m.M22},{m.M31},{m.M32}";
    }

    public void ReadFromJson(JsonObject json)
    {
        JsonObject nodesJson = json[nameof(Nodes)]!.AsObject();
        foreach (NodeViewModel item in Nodes)
        {
            if (nodesJson.TryGetPropertyValue(item.Node.Id.ToString(), out JsonNode? nodeJson))
            {
                item.ReadFromJson(nodeJson!.AsObject());
            }
        }

        if (json.TryGetPropertyValue(nameof(Matrix), out JsonNode? mJson))
        {
            string m = (string)mJson!;
            Matrix.Value = Avalonia.Matrix.Parse(m);
        }
    }

    private void SubscribeNodeConnections(NodeViewModel node)
    {
        var disposables = new CompositeDisposable();
        _nodeSubscriptions[node] = disposables;

        node.Items.ForEachItem(
            (_, item) =>
            {
                if (item is SocketViewModel { Model: ISocket socket })
                {
                    socket.Connected += OnModelSocketConnected;
                    socket.Disconnected += OnModelSocketDisconnected;
                    ScanExistingConnectionsForSocket(item as SocketViewModel);
                }
            },
            (_, item) =>
            {
                if (item is SocketViewModel { Model: ISocket socket } socketVM)
                {
                    socket.Connected -= OnModelSocketConnected;
                    socket.Disconnected -= OnModelSocketDisconnected;
                    CleanUpSocketConnections(socketVM);
                }
            },
            () => { })
        .DisposeWith(disposables);
    }

    private void UnsubscribeNodeConnections(NodeViewModel node)
    {
        if (_nodeSubscriptions.TryGetValue(node, out var disposables))
        {
            // Manually unsubscribe from socket events before disposing ForEachItem
            foreach (NodeItemViewModel item in node.Items)
            {
                if (item is SocketViewModel { Model: ISocket socket })
                {
                    socket.Connected -= OnModelSocketConnected;
                    socket.Disconnected -= OnModelSocketDisconnected;
                }
            }

            disposables.Dispose();
            _nodeSubscriptions.Remove(node);
        }
    }

    private void OnModelSocketConnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        Connection connection = e.Connection;
        // Deduplication: Connected fires on both input and output sides
        foreach (ConnectionViewModel existing in AllConnections.GetMarshal().Value)
        {
            if (existing.Connection == connection)
                return;
        }

        if (FindSocketViewModel(connection.Input) is InputSocketViewModel inputVM
            && FindSocketViewModel(connection.Output) is OutputSocketViewModel outputVM)
        {
            var connVM = new ConnectionViewModel(connection, inputVM, outputVM);
            InsertConnectionInOrder(inputVM, connVM);
            InsertConnectionInOrder(outputVM, connVM);
            AllConnections.Add(connVM);
        }
    }

    private void OnModelSocketDisconnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        Connection connection = e.Connection;
        ConnectionViewModel? connVM = null;
        foreach (ConnectionViewModel existing in AllConnections.GetMarshal().Value)
        {
            if (existing.Connection == connection)
            {
                connVM = existing;
                break;
            }
        }

        if (connVM != null)
        {
            connVM.InputSocketVM.Connections.Remove(connVM);
            connVM.OutputSocketVM.Connections.Remove(connVM);
            AllConnections.Remove(connVM);
            connVM.Dispose();
        }
    }

    private void ScanExistingConnectionsForSocket(SocketViewModel? socketVM)
    {
        if (socketVM is InputSocketViewModel { Model: { } inputSocket })
        {
            if (inputSocket is IListInputSocket listSocket)
            {
                foreach (Connection connection in listSocket.ListConnections)
                {
                    AddConnectionVMIfNotExists(connection);
                }
            }
            else if (inputSocket.Connection is { } connection)
            {
                AddConnectionVMIfNotExists(connection);
            }
        }
        else if (socketVM is OutputSocketViewModel { Model: { } outputSocket })
        {
            foreach (Connection connection in outputSocket.Connections)
            {
                AddConnectionVMIfNotExists(connection);
            }
        }
    }

    private void AddConnectionVMIfNotExists(Connection connection)
    {
        foreach (ConnectionViewModel existing in AllConnections.GetMarshal().Value)
        {
            if (existing.Connection == connection)
                return;
        }

        if (FindSocketViewModel(connection.Input) is InputSocketViewModel inputVM
            && FindSocketViewModel(connection.Output) is OutputSocketViewModel outputVM)
        {
            var connVM = new ConnectionViewModel(connection, inputVM, outputVM);
            InsertConnectionInOrder(inputVM, connVM);
            InsertConnectionInOrder(outputVM, connVM);
            AllConnections.Add(connVM);
        }
    }

    private static void InsertConnectionInOrder(SocketViewModel socketVM, ConnectionViewModel connVM)
    {
        if (socketVM.Model is IListSocket listSocket)
        {
            int modelIndex = listSocket.ListConnections.IndexOf(connVM.Connection);
            if (modelIndex >= 0 && modelIndex <= socketVM.Connections.Count)
            {
                socketVM.Connections.Insert(modelIndex, connVM);
            }
            else
            {
                socketVM.Connections.Add(connVM);
            }
        }
        else
        {
            socketVM.Connections.Add(connVM);
        }
    }

    private void CleanUpNodeConnections(NodeViewModel node)
    {
        for (int i = AllConnections.Count - 1; i >= 0; i--)
        {
            ConnectionViewModel connVM = AllConnections[i];
            bool isInputFromNode = node.Items.Contains(connVM.InputSocketVM);
            bool isOutputFromNode = node.Items.Contains(connVM.OutputSocketVM);

            if (isInputFromNode || isOutputFromNode)
            {
                connVM.InputSocketVM.Connections.Remove(connVM);
                connVM.OutputSocketVM.Connections.Remove(connVM);
                AllConnections.RemoveAt(i);
                connVM.Dispose();
            }
        }
    }

    private void CleanUpSocketConnections(SocketViewModel socketVM)
    {
        for (int i = AllConnections.Count - 1; i >= 0; i--)
        {
            ConnectionViewModel connVM = AllConnections[i];
            if (connVM.InputSocketVM == socketVM || connVM.OutputSocketVM == socketVM)
            {
                connVM.InputSocketVM.Connections.Remove(connVM);
                connVM.OutputSocketVM.Connections.Remove(connVM);
                AllConnections.RemoveAt(i);
                connVM.Dispose();
            }
        }
    }
}

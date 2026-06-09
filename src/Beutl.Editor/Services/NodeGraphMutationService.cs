using System.Diagnostics.CodeAnalysis;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.Editor.Services;

public sealed class NodeGraphMutationService : INodeGraphMutationService
{
    private readonly HistoryManager _historyManager;

    public NodeGraphMutationService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public bool AddNode(GraphModel graph, GraphNode node, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);

        if (graph is GraphGroup group)
        {
            // At most one GroupInput / GroupOutput per GraphGroup; silently
            // reject duplicates so callers need not repeat the type-check.
            if ((node is GroupInput && group.Nodes.Any(x => x is GroupInput))
                || (node is GroupOutput && group.Nodes.Any(x => x is GroupOutput)))
            {
                return false;
            }
        }

        node.Position = (x, y);
        graph.Nodes.Add(node);
        _historyManager.Commit(CommandNames.AddNode);
        return true;
    }

    public void RemoveNode(GraphModel graph, GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);

        // Snapshot touching connections first so disconnect calls don't
        // invalidate the iteration.
        Connection[] touching = node.Items
            .SelectMany(i => i switch
            {
                IOutputPort output => output.Connections,
                IListPort list => list.Connections,
                IInputPort { Connection: var connection } => [connection],
                _ => [],
            })
            .Select(conn => graph.AllConnections.FirstOrDefault(a => a.Id == conn.Id))
            .Where(a => a is not null)
            .ToArray()!;

        foreach (Connection connection in touching)
        {
            graph.Disconnect(connection);
        }

        graph.Nodes.Remove(node);
        _historyManager.Commit(CommandNames.RemoveNode);
    }

    public void MoveNodes(IReadOnlyList<(GraphNode Node, double X, double Y)> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
        if (moves.Count == 0) return;

        foreach ((GraphNode node, double x, double y) in moves)
        {
            node.Position = (x, y);
        }

        _historyManager.Commit(CommandNames.MoveNode);
    }

    public bool RenameNode(GraphNode node, string newName)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(newName);
        if (node.Name == newName) return false;

        node.Name = newName;
        _historyManager.Commit(CommandNames.RenameNode);
        return true;
    }

    public bool RemovePort(GraphNode owner, INodePort port)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(port);
        if (port is not IDynamicPort dynamicPort) return false;

        GraphModel? graph = owner.FindHierarchicalParent<GraphModel>();
        if (graph is null) return false;

        Reference<Connection>[] connections = port switch
        {
            IOutputPort output => [.. output.Connections],
            IListPort list => [.. list.Connections],
            IInputPort { Connection: var connection } => [connection],
            _ => [],
        };

        foreach (Reference<Connection> connection in connections)
        {
            if (connection.Value != null) graph.Disconnect(connection.Value);
        }

        owner.Items.Remove(dynamicPort);
        _historyManager.Commit(CommandNames.RemovePort);
        return true;
    }

    public bool MoveConnectionSlot(INodePort port, int oldIndex, int newIndex)
    {
        ArgumentNullException.ThrowIfNull(port);
        if (port is not IListPort listPort) return false;

        listPort.MoveConnection(oldIndex, newIndex);
        _historyManager.Commit(CommandNames.MoveConnection);
        return true;
    }

    public bool RenamePort(INodePort port, string newName)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(newName);
        if (port.Name == newName) return false;

        port.Name = newName;
        _historyManager.Commit(CommandNames.RenamePort);
        return true;
    }

    public NodeConnectOutcome TryConnect(GraphModel graph,
        GraphNode node1, INodePort? port1,
        GraphNode node2, INodePort? port2)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node1);
        ArgumentNullException.ThrowIfNull(node2);

        // Case 1: one side unset, the other a dynamic-port node — materialize
        // a new port on that node.
        if (port1 is null ^ port2 is null)
        {
            IDynamicPortNode? dynamicNode = null;
            INodePort? mate = null;
            if (node1 is IDynamicPortNode d1)
            {
                dynamicNode = d1;
                mate = port2;
            }
            else if (node2 is IDynamicPortNode d2)
            {
                dynamicNode = d2;
                mate = port1;
            }

            if (dynamicNode is not null && mate is not null && dynamicNode.AddNodePort(mate, out _))
            {
                _historyManager.Commit(CommandNames.AddPort);
                return NodeConnectOutcome.PortAdded;
            }

            return NodeConnectOutcome.None;
        }

        // Case 2: both ports are present — connect if direction-compatible.
        if (port1 is not null && port2 is not null
            && SortPortDirection(port1, port2, out IInputPort? input, out IOutputPort? output))
        {
            graph.Connect(input, output);
            _historyManager.Commit(CommandNames.ConnectPort);
            return NodeConnectOutcome.Connected;
        }

        return NodeConnectOutcome.None;
    }

    public bool TryDisconnect(GraphModel graph, INodePort port1, INodePort port2, Connection? hint = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(port1);
        ArgumentNullException.ThrowIfNull(port2);

        Connection? connection = hint;
        if (connection is null
            && SortPortDirection(port1, port2, out IInputPort? input, out IOutputPort? output))
        {
            connection = graph.AllConnections.FirstOrDefault(
                c => c.Input.Id == input.Id && c.Output.Id == output.Id);
        }

        if (connection is null) return false;

        graph.Disconnect(connection);
        _historyManager.Commit(CommandNames.DisconnectPort);
        return true;
    }

    public bool DisconnectAll(GraphModel graph, INodePort port)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(port);

        Reference<Connection>[]? connections = port switch
        {
            IOutputPort output => [.. output.Connections],
            IListPort list => [.. list.Connections],
            _ => null,
        };

        if (connections is not null)
        {
            bool anySevered = false;
            foreach (Reference<Connection> connection in connections)
            {
                if (connection.Value != null)
                {
                    graph.Disconnect(connection.Value);
                    anySevered = true;
                }
            }

            if (anySevered)
            {
                _historyManager.Commit(CommandNames.DisconnectPort);
                return true;
            }

            return false;
        }

        if (port is IInputPort { Connection.Value: { } singleConnection })
        {
            graph.Disconnect(singleConnection);
            _historyManager.Commit(CommandNames.DisconnectPort);
            return true;
        }

        return false;
    }

    public void DisconnectConnection(GraphModel graph, Connection connection)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(connection);

        graph.Disconnect(connection);
        _historyManager.Commit(CommandNames.DisconnectPort);
    }

    private static bool SortPortDirection(INodePort a, INodePort b,
        [NotNullWhen(true)] out IInputPort? input, [NotNullWhen(true)] out IOutputPort? output)
    {
        if (a is IInputPort ai && b is IOutputPort bo)
        {
            input = ai;
            output = bo;
            return true;
        }
        if (b is IInputPort bi && a is IOutputPort ao)
        {
            input = bi;
            output = ao;
            return true;
        }

        input = null;
        output = null;
        return false;
    }
}

using System.Diagnostics.CodeAnalysis;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;

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

        if (!IsEditable(graph) || !CanAddNode(graph, node)) return false;

        node.Position.Value = (x, y);
        graph.Nodes.Add(node);
        _historyManager.Commit(CommandNames.AddNode);
        return true;
    }

    public bool AddNodeAndConnect(GraphModel graph, GraphNode node, double x, double y,
        INodePort existingPort, INodePort? newPort)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(existingPort);

        if (!IsEditable(graph)
            || !CanAddNode(graph, node)
            || existingPort.FindHierarchicalParent<GraphNode>() is not { } existingNode
            || existingNode.FindHierarchicalParent<GraphModel>() != graph
            || newPort != null && newPort.FindHierarchicalParent<GraphNode>() != node
            || existingPort is IInputPort { Connection.Value.IsNull: false } and not IListInputPort)
            return false;

        try
        {
            _historyManager.ExecuteInTransaction(() =>
            {
                node.Position.Value = (x, y);
                graph.Nodes.Add(node);
                if (ConnectCore(graph, existingNode, existingPort, node, newPort) == NodeConnectOutcome.None)
                    throw new NodeConnectionRejectedException();
            }, CommandNames.AddNode);
            return true;
        }
        catch (NodeConnectionRejectedException)
        {
            return false;
        }
    }

    // Edits are refused while the clip holding the graph is locked or recovery keeps its file, as in the other services.
    private static bool IsEditable(IHierarchical owner)
        => ElementEditability.IsEditable(owner.FindHierarchicalParent<Element>());

    private static bool CanAddNode(GraphModel graph, GraphNode node)
    {
        // At most one GroupInput / GroupOutput per GraphGroup.
        return graph is not GraphGroup group
               || (node is not GroupInput || group.Nodes.All(x => x is not GroupInput))
               && (node is not GroupOutput || group.Nodes.All(x => x is not GroupOutput));
    }

    public void RemoveNode(GraphModel graph, GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        // Removing what could not be loaded repairs a recovery-protected clip, so it stays allowed.
        Element? element = graph.FindHierarchicalParent<Element>();
        if (!ElementEditability.CanRepair(element, node)) return;

        // Snapshot touching connections first so disconnect calls don't
        // invalidate the iteration.
        Connection[] touching = node.EnumerateMembers()
            .SelectMany<INodeMember, Reference<Connection>>(i => i switch
            {
                IOutputPort output => output.Connections,
                IListPort list => list.Connections,
                IInputPort { Connection.Value: var connection } => [connection],
                _ => [],
            })
            .Select(conn => graph.AllConnections.FirstOrDefault(a => a.Id.Value == conn.Id))
            .Where(a => a is not null)
            .ToArray()!;
        // A missing plugin's node has no ports; its connections name the ports it was saved with.
        if (node is FallbackGraphNode fallback)
        {
            HashSet<Guid> ports = [.. fallback.SavedPortIds()];
            touching = [.. graph.AllConnections.Where(c => ports.Contains(c.Input.Value.Id) || ports.Contains(c.Output.Value.Id))];
        }

        foreach (Connection connection in touching)
        {
            graph.Disconnect(connection);
        }

        graph.Nodes.Remove(node);
        if (element != null) ElementRecoveryService.TryCompleteRepair(element, _historyManager);

        _historyManager.Commit(CommandNames.RemoveNode);
    }

    public void MoveNodes(IReadOnlyList<(GraphNode Node, double X, double Y)> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
        if (moves.Count == 0 || moves.Any(move => !IsEditable(move.Node))) return;

        foreach ((GraphNode node, double x, double y) in moves)
        {
            node.Position.Value = (x, y);
        }

        _historyManager.Commit(CommandNames.MoveNode);
    }

    public bool RenameNode(GraphNode node, string newName)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(newName);
        if (node.Name.Value == newName || !IsEditable(node)) return false;

        node.Name.Value = newName;
        _historyManager.Commit(CommandNames.RenameNode);
        return true;
    }

    public bool RemovePort(GraphNode owner, INodePort port)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(port);
        if (port is not IDynamicPort dynamicPort || !IsEditable(owner)) return false;

        GraphModel? graph = owner.FindHierarchicalParent<GraphModel>();
        if (graph is null) return false;

        Reference<Connection>[] connections = port switch
        {
            IOutputPort output => [.. output.Connections],
            IListPort list => [.. list.Connections],
            IInputPort { Connection.Value: var connection } => [connection],
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
        if (port is not IListPort listPort || !IsEditable(port)) return false;

        listPort.MoveConnection(oldIndex, newIndex);
        _historyManager.Commit(CommandNames.MoveConnection);
        return true;
    }

    public bool RewireListSlot(GraphModel graph, Connection connection, GraphNode node, INodePort? port)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(node);
        if (!IsEditable(graph)
            || connection.Input.Value.Value is not IListInputPort list
            || list.FindHierarchicalParent<GraphNode>() is not { } owner
            || port != null && ReferenceEquals(connection.Output.Value.Value, port))
            return false;

        int slot = list.Connections.Index().FirstOrDefault(i => i.Item.Id == connection.Id.Value, (-1, default)).Index;
        if (slot < 0) return false;

        try
        {
            _historyManager.ExecuteInTransaction(() =>
            {
                graph.Disconnect(connection);
                if (ConnectCore(graph, owner, list, node, port) == NodeConnectOutcome.None)
                    throw new NodeConnectionRejectedException();

                // The new connection is appended; it takes the slot of the one it replaces.
                int last = list.Connections.Count - 1;
                if (last != slot) list.MoveConnection(last, slot);
            }, CommandNames.MoveConnection);
            return true;
        }
        catch (NodeConnectionRejectedException)
        {
            return false;
        }
    }

    public bool RenamePort(INodePort port, string newName)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(newName);
        if (port.Name.Value == newName || !IsEditable(port)) return false;

        port.Name.Value = newName;
        _historyManager.Commit(CommandNames.RenamePort);
        return true;
    }

    public bool CanConnect(GraphModel graph, INodePort port1, INodePort port2)
        => IsEditable(graph) && TryGetConnectablePorts(port1, port2, out _, out _);

    public NodeConnectOutcome TryConnect(GraphModel graph,
        GraphNode node1, INodePort? port1,
        GraphNode node2, INodePort? port2)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node1);
        ArgumentNullException.ThrowIfNull(node2);
        if (!IsEditable(graph)) return NodeConnectOutcome.None;

        NodeConnectOutcome outcome = ConnectCore(graph, node1, port1, node2, port2);
        if (outcome == NodeConnectOutcome.PortAdded) _historyManager.Commit(CommandNames.AddPort);
        else if (outcome == NodeConnectOutcome.Connected) _historyManager.Commit(CommandNames.ConnectPort);
        return outcome;
    }

    private static NodeConnectOutcome ConnectCore(GraphModel graph,
        GraphNode node1, INodePort? port1,
        GraphNode node2, INodePort? port2)
    {
        // Case 1: one side unset, the other a dynamic-port node — materialize
        // a new port on that node.
        if (port1 is null ^ port2 is null)
        {
            IDynamicPortNode? dynamicNode = null;
            INodePort? mate = null;
            if (port1 is null && node1 is IDynamicPortNode d1)
            {
                dynamicNode = d1;
                mate = port2;
            }
            else if (port2 is null && node2 is IDynamicPortNode d2)
            {
                dynamicNode = d2;
                mate = port1;
            }

            if (mate is IInputPort nestedInput && !CanAcceptConnection(nestedInput))
            {
                return NodeConnectOutcome.None;
            }

            if (dynamicNode is not null && mate is not null && dynamicNode.AddNodePort(mate, out _))
            {
                return NodeConnectOutcome.PortAdded;
            }

            return NodeConnectOutcome.None;
        }

        // Case 2: both ports are present — connect if direction-compatible.
        if (port1 is not null && port2 is not null
            && TryGetConnectablePorts(port1, port2, out IInputPort? input, out IOutputPort? output))
        {
            graph.Connect(input, output);
            return NodeConnectOutcome.Connected;
        }

        return NodeConnectOutcome.None;
    }

    private static bool TryGetConnectablePorts(INodePort a, INodePort b,
        [NotNullWhen(true)] out IInputPort? input, [NotNullWhen(true)] out IOutputPort? output)
        => SortPortDirection(a, b, out input, out output) && CanAcceptConnection(input);

    private static bool CanAcceptConnection(IInputPort input)
    {
        if (input is not IListInputPort && !input.Connection.Value.IsNull)
            return false;
        return input.FindHierarchicalParent<GraphNode>() is not { } owner || owner.CanConnectInput(input);
    }

    public bool TryDisconnect(GraphModel graph, INodePort port1, INodePort port2, Connection? hint = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(port1);
        ArgumentNullException.ThrowIfNull(port2);
        if (!IsEditable(graph)) return false;

        Connection? connection = hint;
        if (connection is null
            && SortPortDirection(port1, port2, out IInputPort? input, out IOutputPort? output))
        {
            connection = graph.AllConnections.FirstOrDefault(
                c => c.Input.Value.Id == input.Id.Value && c.Output.Value.Id == output.Id.Value);
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
        if (!IsEditable(graph)) return false;

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

        if (port is IInputPort { Connection.Value.Value: { } singleConnection })
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
        if (!IsEditable(graph)) return;

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

    private sealed class NodeConnectionRejectedException : Exception;
}

using System.Diagnostics.CodeAnalysis;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

internal enum NodeConnectionRejection
{
    None,
    SameDirection,
    InputOccupied,
    InputLocked,
    WouldCloseCycle,
    NotDynamicPortNode,
    WrongSide,
    NoPortType
}

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
            || existingPort is IInputPort input && IsOccupied(input))
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

    // The rules below are shared with the agent toolkit, which applies several edits in one history entry.
    internal static bool CanAddNode(GraphModel graph, GraphNode node)
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

        RemoveNodesCore(graph, [node], element);

        _historyManager.Commit(CommandNames.RemoveNode);
    }

    public void RemoveNode(GraphModel graph, IReadOnlyList<GraphNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodes);
        Element? element = graph.FindHierarchicalParent<Element>();
        GraphNode[] removable = [.. nodes.Distinct()
            .Where(node => graph.Nodes.Contains(node) && ElementEditability.CanRepair(element, node))];
        if (removable.Length == 0) return;

        _historyManager.ExecuteInTransaction(() => RemoveNodesCore(graph, removable, element), CommandNames.RemoveNode);
    }

    private void RemoveNodesCore(GraphModel graph, IReadOnlyList<GraphNode> nodes, Element? element)
    {
        foreach (GraphNode node in nodes)
            RemoveNodeCore(graph, node);
        if (element != null) ElementRecoveryService.TryCompleteRepair(element, _historyManager);
    }

    // Severs every connection of the node, then removes it, without committing.
    internal static void RemoveNodeCore(GraphModel graph, GraphNode node)
    {
        // Snapshot touching connections first so disconnect calls don't
        // invalidate the iteration.
        Connection[] touching = node is FallbackGraphNode fallback
            ? [.. graph.AllConnections.Where(c => graph.FindSavedFallbackPort(c, input: true)?.Node == fallback
                || graph.FindSavedFallbackPort(c, input: false)?.Node == fallback)]
            : [.. node.EnumerateMembers()
                .SelectMany(GraphModel.GetPortConnections)
                .Select(conn => graph.AllConnections.FirstOrDefault(a => a.Id.Value == conn.Id))
                .OfType<Connection>()
                // A connection a node makes to itself is listed by both of its ports.
                .Distinct()];

        foreach (Connection connection in touching)
        {
            graph.Disconnect(connection);
        }

        graph.Nodes.Remove(node);
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

        graph.DisconnectPort(port);
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

        ConnectionStatus status = connection.Status.Value;
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
            // History leaves Status out, so the rollback keeps the Disconnected that the removal set.
            connection.Status.Value = status;
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
        => IsEditable(graph) && TryGetConnectablePorts(graph, port1, port2, out _, out _);

    public bool CanConnect(GraphModel graph,
        GraphNode node1, INodePort? port1,
        GraphNode node2, INodePort? port2)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node1);
        ArgumentNullException.ThrowIfNull(node2);
        if (!IsEditable(graph)) return false;

        return (port1, port2) switch
        {
            (null, { } mate) => CanAddPort(graph, node1, mate),
            ({ } mate, null) => CanAddPort(graph, node2, mate),
            ({ } first, { } second) => TryGetConnectablePorts(graph, first, second, out _, out _),
            _ => false,
        };
    }

    // A dynamic-port node adds a port on the side it offers, for a mate on the opposite side.
    private static bool CanAddPort(GraphModel graph, GraphNode node, INodePort mate)
        => CheckAddPort(graph, node, mate, grownOnOutputSide: mate is IInputPort) == NodeConnectionRejection.None;

    internal static NodeConnectionRejection CheckAddPort(GraphModel graph, GraphNode node, INodePort mate, bool grownOnOutputSide)
    {
        if (node is not IDynamicPortNode dynamicNode)
            return NodeConnectionRejection.NotDynamicPortNode;
        if (grownOnOutputSide ? mate is not IInputPort : mate is not IOutputPort)
            return NodeConnectionRejection.SameDirection;

        NodePortLocation side = grownOnOutputSide ? NodePortLocation.Right : NodePortLocation.Left;
        if (!dynamicNode.PossibleLocation.HasFlag(side))
            return NodeConnectionRejection.WrongSide;
        if (mate.AssociatedType is null)
            return NodeConnectionRejection.NoPortType;

        if (mate is IInputPort input)
        {
            NodeConnectionRejection rejection = CheckInput(input);
            if (rejection != NodeConnectionRejection.None) return rejection;
        }

        return WouldNewPortCloseCycle(graph, node, mate)
            ? NodeConnectionRejection.WouldCloseCycle
            : NodeConnectionRejection.None;
    }

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
        if (port1 is null || port2 is null)
        {
            GraphNode node = port1 is null ? node1 : node2;
            INodePort? mate = port1 ?? port2;
            return mate is not null && CanAddPort(graph, node, mate)
                && ((IDynamicPortNode)node).AddNodePort(mate, out _)
                ? NodeConnectOutcome.PortAdded
                : NodeConnectOutcome.None;
        }

        if (TryGetConnectablePorts(graph, port1, port2, out IInputPort? input, out IOutputPort? output))
        {
            graph.Connect(input, output);
            return NodeConnectOutcome.Connected;
        }

        return NodeConnectOutcome.None;
    }

    private static bool TryGetConnectablePorts(GraphModel graph, INodePort a, INodePort b,
        [NotNullWhen(true)] out IInputPort? input, [NotNullWhen(true)] out IOutputPort? output)
        => CheckConnect(graph, a, b, out input, out output) == NodeConnectionRejection.None
           && input is not null && output is not null;

    internal static NodeConnectionRejection CheckConnect(GraphModel graph, INodePort a, INodePort b,
        out IInputPort? input, out IOutputPort? output)
    {
        if (!SortPortDirection(a, b, out input, out output))
            return NodeConnectionRejection.SameDirection;

        NodeConnectionRejection rejection = CheckInput(input);
        if (rejection != NodeConnectionRejection.None) return rejection;

        return WouldCloseCycle(graph, input, output)
            ? NodeConnectionRejection.WouldCloseCycle
            : NodeConnectionRejection.None;
    }

    private static bool IsOccupied(IInputPort input) => input is not IListInputPort && !input.Connection.Value.IsNull;

    private static NodeConnectionRejection CheckInput(IInputPort input)
    {
        if (IsOccupied(input)) return NodeConnectionRejection.InputOccupied;
        return input.FindHierarchicalParent<GraphNode>() is { } owner && !owner.CanConnectInput(input)
            ? NodeConnectionRejection.InputLocked
            : NodeConnectionRejection.None;
    }

    // A graph evaluates its nodes in dependency order, so it skips every node on a cycle.
    private static bool WouldCloseCycle(GraphModel graph, IInputPort input, IOutputPort output)
    {
        if (input.FindHierarchicalParent<GraphNode>() is not { } consumer
            || output.FindHierarchicalParent<GraphNode>() is not { } producer)
        {
            return false;
        }

        return WouldCloseCycle(graph, consumer, producer);
    }

    // The new port makes the node the producer of an input mate, or the consumer of an output mate.
    private static bool WouldNewPortCloseCycle(GraphModel graph, GraphNode node, INodePort mate)
        => mate.FindHierarchicalParent<GraphNode>() is { } mateNode
            && (mate is IInputPort
                ? WouldCloseCycle(graph, mateNode, node)
                : WouldCloseCycle(graph, node, mateNode));

    private static bool WouldCloseCycle(GraphModel graph, GraphNode consumer, GraphNode producer)
    {
        // This runs on every pointer move over a candidate port during a wire drag.
        ILookup<GraphNode?, GraphNode?> consumers = graph.AllConnections.ToLookup(
            connection => connection.Output.Value.Value?.FindHierarchicalParent<GraphNode>(),
            connection => connection.Input.Value.Value?.FindHierarchicalParent<GraphNode>());
        var visited = new HashSet<GraphNode>();
        var pending = new Stack<GraphNode>([consumer]);
        while (pending.TryPop(out GraphNode? node))
        {
            if (node == producer) return true;
            if (!visited.Add(node)) continue;

            foreach (GraphNode? next in consumers[node])
            {
                if (next is not null) pending.Push(next);
            }
        }

        return false;
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

        return connection != null && DisconnectConnectionCore(graph, connection);
    }

    public bool DisconnectAll(GraphModel graph, INodePort port)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(port);
        if (!IsEditable(graph) || !graph.DisconnectPort(port)) return false;

        _historyManager.Commit(CommandNames.DisconnectPort);
        return true;
    }

    public void DisconnectConnection(GraphModel graph, Connection connection)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(connection);
        if (IsEditable(graph)) DisconnectConnectionCore(graph, connection);
    }

    private bool DisconnectConnectionCore(GraphModel graph, Connection connection)
    {
        if (!graph.AllConnections.Contains(connection)) return false;
        graph.Disconnect(connection);
        _historyManager.Commit(CommandNames.DisconnectPort);
        return true;
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

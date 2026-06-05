using Beutl.NodeGraph;

namespace Beutl.Editor.Services;

/// <summary>
/// Mutation operations on a <see cref="GraphModel"/>: add/remove/move/rename
/// nodes, connect/disconnect ports, and the dynamic-port add/remove flow that
/// is unique to <see cref="GroupInput"/> / <see cref="GroupOutput"/>. The
/// validation branches (GroupInput uniqueness, port-direction sorting, cascade
/// disconnects when a node or dynamic port is removed) were previously scattered
/// across <c>NodeGraphViewModel</c>, <c>GraphNodeViewModel</c>, and
/// <c>NodePortViewModel</c> with ~11 distinct <c>HistoryManager.Commit</c>
/// sites and no shared test surface.
/// </summary>
public interface INodeGraphMutationService
{
    /// <summary>Adds <paramref name="node"/> to <paramref name="graph"/> at the
    /// given position. Rejects (returns false, no commit) when a duplicate
    /// <see cref="GroupInput"/> / <see cref="GroupOutput"/> would be added to a
    /// <see cref="GraphGroup"/>. Commits <c>AddNode</c> on success.</summary>
    bool AddNode(GraphModel graph, GraphNode node, double x, double y);

    /// <summary>Cascade-disconnects every connection touching
    /// <paramref name="node"/>, removes the node from
    /// <paramref name="graph"/>, and commits one <c>RemoveNode</c> entry.</summary>
    void RemoveNode(GraphModel graph, GraphNode node);

    /// <summary>Bulk-writes <see cref="GraphNode.Position"/> for each pair,
    /// then commits one <c>MoveNode</c> entry.</summary>
    void MoveNodes(IReadOnlyList<(GraphNode Node, double X, double Y)> moves);

    /// <summary>Idempotent rename. Returns false and commits nothing when
    /// <paramref name="newName"/> already matches the current name.</summary>
    bool RenameNode(GraphNode node, string newName);

    /// <summary>Removes a dynamic port from <paramref name="owner"/>,
    /// cascade-disconnecting every connection on the port first. Returns
    /// false when <paramref name="port"/> is not an
    /// <see cref="IDynamicPort"/>. Commits <c>RemovePort</c> on success.</summary>
    bool RemovePort(GraphNode owner, INodePort port);

    /// <summary>Reorders a slot inside an <see cref="IListPort"/>. Returns
    /// false when the port is not list-typed. Commits
    /// <c>MoveConnection</c> on success.</summary>
    bool MoveConnectionSlot(INodePort port, int oldIndex, int newIndex);

    /// <summary>Idempotent port rename.</summary>
    bool RenamePort(INodePort port, string newName);

    /// <summary>Three-way connect:
    /// <list type="bullet">
    /// <item>One port unset + opposite side is <see cref="IDynamicPortNode"/>
    /// → add a new port (commits <c>AddPort</c>).</item>
    /// <item>Both ports set with compatible direction (one input + one output)
    /// → connect (commits <c>ConnectPort</c>).</item>
    /// <item>Otherwise → no-op.</item>
    /// </list>
    /// </summary>
    NodeConnectOutcome TryConnect(GraphModel graph,
        GraphNode node1, INodePort? port1,
        GraphNode node2, INodePort? port2);

    /// <summary>Disconnects a specific connection. The hint takes precedence
    /// when supplied; otherwise the service looks up the connection by
    /// matching the two port ids. Commits <c>DisconnectPort</c> on success.</summary>
    bool TryDisconnect(GraphModel graph, INodePort port1, INodePort port2, Connection? hint = null);

    /// <summary>Disconnects every connection on the port (walks
    /// <see cref="IOutputPort"/>, <see cref="IListPort"/>, and
    /// <see cref="IInputPort"/> variants). Commits <c>DisconnectPort</c>
    /// when at least one connection was severed.</summary>
    bool DisconnectAll(GraphModel graph, INodePort port);

    /// <summary>Disconnects a specific known <see cref="Connection"/>.
    /// Commits <c>DisconnectPort</c>.</summary>
    void DisconnectConnection(GraphModel graph, Connection connection);
}

public enum NodeConnectOutcome
{
    /// <summary>Neither path matched — no mutation, no commit.</summary>
    None,
    /// <summary>A new dynamic port was added to an
    /// <see cref="IDynamicPortNode"/>. Commits <c>AddPort</c>.</summary>
    PortAdded,
    /// <summary>The two existing ports were connected. Commits
    /// <c>ConnectPort</c>.</summary>
    Connected,
}

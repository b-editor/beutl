using Beutl.NodeGraph;

namespace Beutl.Editor.Services;

/// <summary>
/// Mutation operations on a <see cref="GraphModel"/>: add/remove/move/rename
/// nodes, connect/disconnect ports, and the dynamic-port flow unique to
/// <see cref="GroupInput"/> / <see cref="GroupOutput"/>. Centralizes the
/// validation branches (GroupInput uniqueness, cascade disconnects) and the
/// many commit sites that were scattered across the node-graph ViewModels.
/// </summary>
public interface INodeGraphMutationService
{
    /// <summary>Adds <paramref name="node"/> at the given position. Returns false
    /// (no commit) for a duplicate <see cref="GroupInput"/> / <see cref="GroupOutput"/>
    /// in a <see cref="GraphGroup"/>; commits <c>AddNode</c> on success.</summary>
    bool AddNode(GraphModel graph, GraphNode node, double x, double y);

    /// <summary>Cascade-disconnects every connection touching
    /// <paramref name="node"/>, removes it, and commits one <c>RemoveNode</c>.</summary>
    void RemoveNode(GraphModel graph, GraphNode node);

    /// <summary>Bulk-writes <see cref="GraphNode.Position"/> for each pair, then
    /// commits one <c>MoveNode</c>.</summary>
    void MoveNodes(IReadOnlyList<(GraphNode Node, double X, double Y)> moves);

    /// <summary>Idempotent rename. Returns false and commits nothing when
    /// <paramref name="newName"/> already matches the current name.</summary>
    bool RenameNode(GraphNode node, string newName);

    /// <summary>Cascade-disconnects then removes a dynamic port. Returns false
    /// when <paramref name="port"/> is not an <see cref="IDynamicPort"/>; commits
    /// <c>RemovePort</c> on success.</summary>
    bool RemovePort(GraphNode owner, INodePort port);

    /// <summary>Reorders a slot inside an <see cref="IListPort"/>. Returns false
    /// when the port is not list-typed; commits <c>MoveConnection</c> on success.</summary>
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

    /// <summary>Disconnects a specific connection — <paramref name="hint"/> wins
    /// when supplied, otherwise it is looked up by matching the two port ids.
    /// Commits <c>DisconnectPort</c> on success.</summary>
    bool TryDisconnect(GraphModel graph, INodePort port1, INodePort port2, Connection? hint = null);

    /// <summary>Disconnects every connection on the port (output / list / input
    /// variants). Commits <c>DisconnectPort</c> when at least one was severed.</summary>
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

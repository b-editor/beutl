using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.AgentToolkit.NodeGraphs;

public sealed record NodeView(
    string Id,
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Name,
    double[] Position,
    IReadOnlyList<MemberView> Members,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? InnerNodeCount = null);

public sealed record MemberView(
    string Name,
    string Kind,
    string ValueType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonNode? Value = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? From = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? To = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CodeOmitted = null);

public sealed record ConnectionView(string From, string To, string Status);

public sealed record PortSummary(string Name, string Kind, string ValueType);

// The graph as an agent reads and addresses it: nodes by Id, ports by '<node Id>.<port name>'.
internal static class NodeGraphView
{
    public static IReadOnlyList<NodeView> DescribeNodes(GraphModel graph, bool includeCode)
    {
        return graph.Nodes.Select(node => new NodeView(
                node.Id.Value.ToString(),
                NodeGraphMembers.FormatType(node.GetType()),
                string.IsNullOrEmpty(node.Name.Value) ? null : node.Name.Value,
                [node.Position.Value.X, node.Position.Value.Y],
                node.EnumerateMembers().Select(member => DescribeMember(graph, node, member, includeCode)).ToArray(),
                node is GroupNode group ? group.Group.Value.Nodes.Count : null))
            .ToArray();
    }

    public static IReadOnlyList<ConnectionView> DescribeConnections(GraphModel graph)
    {
        return graph.AllConnections.Select(connection => new ConnectionView(
                End(connection.Output.Value.Value as INodeMember),
                End(connection.Input.Value.Value as INodeMember),
                StatusName(connection.Status.Value)))
            .ToArray();
    }

    public static IReadOnlyList<PortSummary> DescribePorts(GraphNode node)
    {
        return node.EnumerateMembers()
            .Where(member => member is INodePort || NodeGraphMembers.IsSettable(member))
            .Select(member => new PortSummary(
                NodeGraphMembers.NameOf(node, member),
                NodeGraphMembers.KindOf(member),
                NodeGraphMembers.FormatType(NodeGraphMembers.ValueTypeOf(member))))
            .ToArray();
    }

    // '<node Id>.<port name>', as edit_node_graph takes it.
    public static string End(INodeMember? member)
    {
        if (member?.FindHierarchicalParent<GraphNode>() is not { } node)
        {
            return "?";
        }

        return $"{node.Id.Value}.{NodeGraphMembers.NameOf(node, member)}";
    }

    private static MemberView DescribeMember(GraphModel graph, GraphNode node, INodeMember member, bool includeCode)
    {
        bool omitCode = !includeCode && NodeGraphMembers.IsCode(member);
        return new MemberView(
            NodeGraphMembers.NameOf(node, member),
            NodeGraphMembers.KindOf(member),
            NodeGraphMembers.FormatType(NodeGraphMembers.ValueTypeOf(member)),
            omitCode ? null : NodeGraphMembers.ReadValue(member),
            Sources(graph, member),
            Targets(graph, member),
            omitCode ? true : null);
    }

    private static IReadOnlyList<string>? Sources(GraphModel graph, INodeMember member)
    {
        IEnumerable<Guid> ids = member switch
        {
            IListInputPort list => list.Connections.Select(reference => reference.Id),
            IInputPort { Connection.Value: { IsNull: false } connection } => [connection.Id],
            _ => [],
        };
        string[] sources = ids
            .Select(id => graph.AllConnections.FirstOrDefault(connection => connection.Id.Value == id))
            .Where(connection => connection is not null)
            .Select(connection => End(connection!.Output.Value.Value as INodeMember))
            .ToArray();
        return sources.Length > 0 ? sources : null;
    }

    private static IReadOnlyList<string>? Targets(GraphModel graph, INodeMember member)
    {
        if (member is not IOutputPort output || output.Connections.Count == 0)
        {
            return null;
        }

        return output.Connections
            .Select(reference => graph.AllConnections.FirstOrDefault(connection => connection.Id.Value == reference.Id))
            .Where(connection => connection is not null)
            .Select(connection => End(connection!.Input.Value.Value as INodeMember))
            .ToArray();
    }

    private static string StatusName(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Success => "ok",
        ConnectionStatus.Convert => "converted",
        ConnectionStatus.Error => "error",
        ConnectionStatus.Disconnected => "disconnected",
        _ => "connected",
    };
}

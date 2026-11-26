using System.ComponentModel;
using System.Text.Json.Serialization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.NodeGraphs;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media.Decoding;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record ListNodeTypesResponse(
    string SchemaVersion,
    IReadOnlyList<NodeTypeSummary> NodeTypes,
    IReadOnlyList<string> Categories,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? UnmatchedTypes,
    string UsageHint);

public sealed record ReadNodeGraphResponse(
    string SchemaVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<NodeGraphSummary>? Graphs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? GraphId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? OwnerId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? OwnerType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<NodeView>? Nodes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ConnectionView>? Connections = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<NodeGraphDiagnostic>? Diagnostics = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? UsageHint = null);

public sealed record CreatedNodeSummary(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Ref,
    string Id,
    string Type);

public sealed record EditNodeGraphResponse(
    string SchemaVersion,
    bool Applied,
    bool Valid,
    string GraphId,
    IReadOnlyList<CreatedNodeSummary> Created,
    IReadOnlyList<NodeGraphDiagnostic> Diagnostics,
    IReadOnlyDictionary<string, IReadOnlyList<PortSummary>> Ports,
    string Hint);

// Builds and edits node graphs, the declarative way to compose GPU shader passes and other node-based content.
[McpServerToolType]
public sealed class NodeGraphTools(AgentSessionManager sessions, DecoderRegistry? decoders = null) : ToolBase
{
    // What undo and read_history call one edit_node_graph call.
    public const string HistoryEntryName = "Agent node graph edit";

    [McpServerTool(Name = "get_node_graph_guide")]
    [Description("Returns the contract for node graphs and GPU shader passes without C#: where a graph lives (NodeGraphFilterEffect or NodeGraphDrawable), how edit_node_graph addresses nodes and ports, GLSL rules and how a pass's code becomes its ports, coordinates, padding and resolution, color and formats, time, limits, and minimal operation lists for edit_node_graph. Read it before building a graph.")]
    public ToolResult<NodeGraphGuideResponse> GetNodeGraphGuide()
        => Execute(NodeGraphGuide.Create);

    [McpServerTool(Name = "list_node_types")]
    [Description("Lists the node types a graph can hold, with their categories, which are the editor's menu names in its UI language. Without filters it returns names only; with category or type (a name or a comma-separated list) it adds each type's members: name, kind (input, output, listInput, property, monitor), value type, default and enum names. A shader pass also gets the ports its code declares, from the code it starts with.")]
    public ToolResult<ListNodeTypesResponse> ListNodeTypes(
        [Description("A category as categories lists it, or the start of one; a category takes in its subcategories.")]
        string? category = null,
        [Description("A type name or $type discriminator, or a comma-separated list of them.")]
        string? type = null,
        [Description("Whether to include members. Defaults to true when category or type is given.")]
        bool? includeMembers = null)
    {
        return Execute(() =>
        {
            NodeTypeCatalog.EnsureBuiltInNodesRegistered();
            IReadOnlyList<NodeTypeEntry> entries = NodeTypeCatalog.GetEntries();
            string[] categories = entries.Select(entry => entry.Category).Distinct(StringComparer.Ordinal).ToArray();
            IEnumerable<NodeTypeEntry> selected = entries;
            List<string>? unmatched = null;
            if (!string.IsNullOrWhiteSpace(category))
            {
                string wanted = category.Trim();
                selected = selected.Where(entry => entry.Category.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                var resolved = new List<NodeTypeEntry>();
                unmatched = [];
                foreach (string token in type.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (NodeTypeCatalog.Resolve(token, out _) is { } entry)
                    {
                        resolved.Add(entry);
                    }
                    else
                    {
                        unmatched.Add(token);
                    }
                }

                selected = selected.Where(entry => resolved.Any(match => match.Type == entry.Type));
            }

            bool members = includeMembers ?? (category is not null || type is not null);
            NodeTypeSummary[] types = selected
                .DistinctBy(entry => entry.Type)
                .Select(entry => NodeTypeCatalog.Describe(entry, members))
                .ToArray();
            if (types.Length == 0)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.UnknownType,
                    $"No node types matched category='{category}' type='{type}'.",
                    type ?? category,
                    $"The categories are {string.Join(", ", categories)}."));
            }

            return new ListNodeTypesResponse(
                SchemaVersion.Current,
                types,
                categories,
                unmatched is { Count: > 0 } ? unmatched : null,
                "Pass a Type name to edit_node_graph's add operation. A pass's In.* and Out.* ports follow its code; get_node_graph_guide explains how.");
        });
    }

    [McpServerTool(Name = "read_node_graph")]
    [Description("Reads a node graph: its nodes with Id, type, name, position and members (kind, value type, value or $kf keyframes, connections as '<node Id>.<port>'), its connections with their status, and diagnostics from a fresh evaluation, such as shader compiler messages with stage and line. Without graphId it lists the scene's graphs. In live MCP, pass sceneId on each call.")]
    public ToolResult<ReadNodeGraphResponse> ReadNodeGraph(
        [Description("The Id of a NodeGraphFilterEffect, a NodeGraphDrawable, or a group or loop node inside one. Omit to list the scene's graphs.")]
        string? graphId = null,
        [Description("Whether to return the text of code members such as a pass's Code. False leaves it out and marks the member codeOmitted.")]
        bool includeCode = true)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            return session.ReadOnSession(() =>
            {
                Scene scene = RequireSceneRoot(session);
                if (graphId is null)
                {
                    return new ReadNodeGraphResponse(
                        SchemaVersion.Current,
                        Graphs: NodeGraphLocator.List(scene),
                        UsageHint: "Pass a graphId to read one graph. To add one, add a NodeGraphFilterEffect or NodeGraphDrawable with apply_edit.");
                }

                NodeGraphTarget live = NodeGraphLocator.Locate(scene, graphId);
                // Evaluating writes the monitors and statuses it reads, which the scene keeps for its own renders.
                NodeGraphTarget copy = NodeGraphLocator.Locate(SceneSnapshot.Create(scene), graphId);
                IReadOnlyList<NodeView> nodes = [];
                IReadOnlyList<ConnectionView> connections = [];
                List<NodeGraphDiagnostic> diagnostics = NodeGraphDiagnostics.Evaluate(session.Fonts, decoders, copy, NodeGraphDiagnostics.Label, () =>
                {
                    nodes = NodeGraphView.DescribeNodes(copy.Graph, includeCode);
                    connections = NodeGraphView.DescribeConnections(copy.Graph);
                });
                return new ReadNodeGraphResponse(
                    SchemaVersion.Current,
                    GraphId: graphId,
                    OwnerId: live.Owner.Id.Value.ToString(),
                    OwnerType: live.Owner.GetType().Name,
                    Nodes: nodes,
                    Connections: connections,
                    Diagnostics: diagnostics);
            });
        });
    }

    [McpServerTool(Name = "edit_node_graph")]
    [Description("Edits a node graph with a list of operations (add, update, remove, connect, disconnect), applied in order as one undoable history entry. Operations run on a copy of the scene first: an operation that cannot apply is refused with its index, and an edit that brings an error the graph did not have, such as GLSL that does not compile (returned with its stage and line), a value that does not convert or a missing connection, is not applied. The response lists the new nodes' Ids, the diagnostics (isNew marks what the edit brought) and the ports of the nodes it touched. Pass validateOnly:true to check without applying. get_node_graph_guide has the contract and examples. In live MCP, pass sceneId on each call.")]
    public ToolResult<EditNodeGraphResponse> EditNodeGraph(
        [Description("The Id of a NodeGraphFilterEffect, a NodeGraphDrawable, or a group or loop node inside one.")]
        string graphId,
        [Description("The operations, in order.")]
        IReadOnlyList<NodeGraphOperation> operations,
        [Description("Check the operations on a copy and report, without applying them.")]
        bool validateOnly = false,
        CancellationToken cancellationToken = default)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            IReadOnlyList<NodeGraphOperation> steps = operations ?? [];
            return session.EditOnSession(() =>
            {
                Scene scene = RequireSceneRoot(session);
                NodeGraphTarget live = NodeGraphLocator.Locate(scene, graphId);
                if (steps.Count == 0)
                {
                    return new EditNodeGraphResponse(
                        SchemaVersion.Current,
                        Applied: false,
                        Valid: true,
                        graphId,
                        Created: [],
                        Diagnostics: [],
                        Ports: new Dictionary<string, IReadOnlyList<PortSummary>>(),
                        Hint: "No operations were provided. Nothing was applied.");
                }

                Rational? frameRate = scene.GetFrameRate();
                Guid[] nodeIds = NodeGraphEditor.MintNodeIds(steps);

                NodeGraphTarget copy = NodeGraphLocator.Locate(SceneSnapshot.Create(scene), graphId);
                List<NodeGraphDiagnostic> before = NodeGraphDiagnostics.Evaluate(session.Fonts, decoders, copy, NodeGraphDiagnostics.Label);
                var editor = new NodeGraphEditor(copy.Graph, frameRate);
                editor.Apply(steps, nodeIds);
                string Label(GraphNode node) => editor.RefOf(node) ?? NodeGraphDiagnostics.Label(node);
                IReadOnlyDictionary<string, IReadOnlyList<PortSummary>> ports = new Dictionary<string, IReadOnlyList<PortSummary>>();
                List<NodeGraphDiagnostic> after = NodeGraphDiagnostics.Evaluate(session.Fonts, decoders, copy, Label, () =>
                {
                    ports = editor.Touched
                        .Where(node => IsInside(node, copy.Graph))
                        .ToDictionary(node => editor.RefOf(node) ?? node.Id.Value.ToString(), NodeGraphView.DescribePorts);
                });
                bool brokeSomething = NodeGraphDiagnostics.MarkNew(before, after);
                CreatedNodeSummary[] created = editor.Created
                    .Where(node => IsInside(node, copy.Graph))
                    .Select(node => new CreatedNodeSummary(editor.RefOf(node), node.Id.Value.ToString(), NodeGraphMembers.FormatType(node.GetType())))
                    .ToArray();

                if (brokeSomething || validateOnly)
                {
                    return new EditNodeGraphResponse(
                        SchemaVersion.Current,
                        Applied: false,
                        Valid: !brokeSomething,
                        graphId,
                        created,
                        after,
                        ports,
                        brokeSomething
                            ? "Nothing was applied: the edit brings the errors marked isNew. Fix them and call again; the created Ids are not in the scene."
                            : "Valid. Nothing was applied; call again without validateOnly to apply.");
                }

                if (!ElementEditability.IsEditable(live.Element))
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.ValidationRejected,
                        "The element that holds this graph is locked, or recovery protects its file, so its graph cannot be edited.",
                        live.Element?.Id.Value.ToString(),
                        "Only the user can unlock an element or a layer."));
                }

                // The copy accepted the same operations, so they apply here too; a failure still rolls the entry back.
                session.History.ExecuteInTransaction(
                    () => new NodeGraphEditor(live.Graph, frameRate).Apply(steps, nodeIds),
                    HistoryEntryName,
                    cancellationToken: cancellationToken);
                session.MarkDirtyIfFileSession();

                return new EditNodeGraphResponse(
                    SchemaVersion.Current,
                    Applied: true,
                    Valid: true,
                    graphId,
                    created,
                    after,
                    ports,
                    "Applied as one history entry; undo reverts the whole call. render_still shows the result, and lists in its warnings what only the GPU could refuse.");
            }, cancellationToken);
        });
    }

    private static bool IsInside(GraphNode node, GraphModel graph)
        => node.EnumerateAncestors<GraphModel>().Contains(graph);
}

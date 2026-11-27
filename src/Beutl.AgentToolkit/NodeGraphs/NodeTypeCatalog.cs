using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Schema;
using Beutl.Editor.Services;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Services;

namespace Beutl.AgentToolkit.NodeGraphs;

public sealed record NodeTypeSummary(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Discriminator,
    string DisplayName,
    string Category,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<NodeMemberSummary>? Members = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Notes = null);

public sealed record NodeMemberSummary(
    string Name,
    string Kind,
    string ValueType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonNode? Default = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? EnumValues = null);

internal sealed record NodeTypeEntry(Type Type, string Name, string Discriminator, string DisplayName, string Category);

// The node types the editor offers, with the same category paths as its add-node menu.
internal static class NodeTypeCatalog
{
    private static readonly object s_lock = new();
    private static readonly Lazy<LibraryService> s_defaultLibrary = new(TypeRegistration.CreateLibraryService);

    // What a node type's ports do that its members cannot say. Keyed by type, like the effect metadata of the schema.
    private static readonly Dictionary<Type, string[]> s_notes = new()
    {
        [typeof(FilterEffectInputNode)] =
        [
            "The layer the effect applies to, as a picture. A NodeGraphFilterEffect without this node passes its layer through and never runs its graph."
        ],
        [typeof(OutputNode)] =
        [
            "What reaches Output is drawn: in a NodeGraphFilterEffect it replaces the layer, in a NodeGraphDrawable it is the drawable's content. An image wire connected here becomes a picture."
        ],
        [typeof(TimeNode)] =
        [
            "Time is the seconds since the element started, Start where the element starts on the timeline and Duration its length, both in seconds; Progress runs from 0 to 1 over the element."
        ],
        [typeof(ShaderFullscreenPassNode)] =
        [
            "Ports named In.* and Out.* come from Code; setting Code makes them match its declarations, keeping the values and connections of ports whose name and type stay.",
            "The built-in vertex stage gives `layout(location = 0) in vec2 uv`, from 0 to 1 across the output. A sampler left unconnected reads the source."
        ],
        [typeof(ShaderComputePassNode)] =
        [
            "Ports named In.* and Out.* come from Code, a compute shader. Writable images and buffers also get an Out.* port.",
            "Without a Dispatch setting it runs one invocation per texel of the first image it writes, rounded up to the local size, so guard with imageSize."
        ],
        [typeof(ShaderRenderPassNode)] =
        [
            "Ports named In.* and Out.* come from Vertex and Fragment. Without a Draw setting it draws 3 vertices."
        ],
        [typeof(ShaderExtendedRenderPassNode)] =
        [
            "Like the render pass, with tessellation and geometry stages. MoltenVK on macOS refuses a geometry stage when the graph runs."
        ],
        [typeof(ShaderRepeatNode)] =
        [
            "Its body is an inner graph that runs Count times; edit it with graphId set to this node's Id, or with the graph field of an operation. Each state output of the body feeds the state input at the same position."
        ],
        [typeof(ShaderReplayNode)] =
        [
            "Its body is an inner graph that runs in fixed steps from ResetTime to the current time, so a seek gives the same picture as playing; edit it with graphId set to this node's Id, or with the graph field of an operation."
        ],
        [typeof(GroupNode)] =
        [
            "Its inner graph holds a GroupInput and a GroupOutput; edit it with graphId set to this node's Id, or with the graph field of an operation. Connecting to GroupInput or GroupOutput without a port name adds a port that the group shows outside."
        ],
        [typeof(GroupInput)] = ["Connect from this node without a port name to add a port matching the input you connect to."],
        [typeof(GroupOutput)] = ["Connect to this node without a port name to add a port matching the output you connect from."]
    };

    // The standalone MCP host does not start the app, which registers the built-in nodes at startup.
    public static void EnsureBuiltInNodesRegistered()
    {
        lock (s_lock)
        {
            if (GraphNodeRegistry.FindItem(typeof(OutputNode)) is null)
            {
                NodesRegistrar.RegisterAll();
            }
        }
    }

    public static IReadOnlyList<NodeTypeEntry> GetEntries(LibraryService? library = null)
    {
        EnsureBuiltInNodesRegistered();
        return Collect(NodeMenuCatalog.GetItems(library ?? s_defaultLibrary.Value));
    }

    private static IReadOnlyList<NodeTypeEntry> Collect(IEnumerable<GraphNodeRegistry.BaseRegistryItem> items)
    {
        var entries = new List<NodeTypeEntry>();
        foreach (GraphNodeRegistry.BaseRegistryItem item in items)
        {
            Collect(item, null, entries);
        }

        return entries;
    }

    // A type by the name list_node_types gives it, its $type discriminator, or its full name.
    public static NodeTypeEntry? Resolve(string token, out IReadOnlyList<NodeTypeEntry> candidates,
        IReadOnlyList<NodeTypeEntry>? entries = null)
    {
        string trimmed = token.Trim();
        if (entries == null)
        {
            EnsureBuiltInNodesRegistered();
            entries = Collect(GraphNodeRegistry.GetRegistered());
        }

        NodeTypeEntry[] matches = entries
            .Where(entry => string.Equals(entry.Name, trimmed, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(entry.Discriminator, trimmed, StringComparison.Ordinal)
                            || string.Equals(entry.Type.FullName, trimmed, StringComparison.Ordinal))
            .DistinctBy(entry => entry.Type)
            .ToArray();
        if (matches.Length == 1)
        {
            candidates = [];
            return matches[0];
        }

        candidates = matches.Length > 1
            ? matches
            : entries.Where(entry => entry.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                                     || trimmed.Contains(entry.Type.Name.Split('`')[0], StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .ToArray();
        return null;
    }

    public static IReadOnlyList<string>? NotesFor(Type type) => s_notes.GetValueOrDefault(type);

    // A list of names leaves out what only a closer look needs: discriminators, members and notes.
    public static NodeTypeSummary Describe(NodeTypeEntry entry, bool includeMembers)
    {
        return new NodeTypeSummary(
            entry.Name,
            includeMembers ? entry.Discriminator : null,
            entry.DisplayName,
            entry.Category,
            includeMembers ? DescribeMembers(entry.Type) : null,
            includeMembers ? NotesFor(entry.Type) : null);
    }

    private static IReadOnlyList<NodeMemberSummary>? DescribeMembers(Type type)
    {
        GraphNode node;
        try
        {
            node = (GraphNode)Activator.CreateInstance(type)!;
        }
        catch (Exception)
        {
            return null;
        }

        return node.EnumerateMembers()
            .Select(member => new NodeMemberSummary(
                NodeGraphMembers.NameOf(node, member),
                NodeGraphMembers.KindOf(member),
                NodeGraphMembers.FormatType(NodeGraphMembers.ValueTypeOf(member)),
                member is INodeMonitor ? null : NodeGraphMembers.ReadValue(member),
                NodeGraphMembers.ValueTypeOf(member) is { } type && NodeGraphMembers.IsSettable(member)
                    ? EnumJsonValueNormalizer.GetEnumNames(type)
                    : null))
            .ToArray();
    }

    private static void Collect(GraphNodeRegistry.BaseRegistryItem item, string? category, List<NodeTypeEntry> entries)
    {
        switch (item)
        {
            case GraphNodeRegistry.RegistryItem single:
                entries.Add(new NodeTypeEntry(
                    single.Type,
                    NodeGraphMembers.FormatType(single.Type),
                    IdentityHelper.WriteDiscriminator(single.Type),
                    single.DisplayName,
                    category ?? "General"));
                break;
            case GraphNodeRegistry.GroupableRegistryItem group:
                string path = category is null ? group.DisplayName : $"{category}/{group.DisplayName}";
                foreach (GraphNodeRegistry.BaseRegistryItem child in group.Items)
                {
                    Collect(child, path, entries);
                }

                break;
        }
    }
}

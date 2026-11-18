using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Engine;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.NodeGraphs;

public sealed record NodeGraphSummary(
    string GraphId,
    string OwnerType,
    string? Name,
    string? ElementId,
    string? ElementName,
    int NodeCount);

// The graph an Id names: Graph is the one to edit, Root the whole graph of the effect or drawable that owns it.
internal sealed record NodeGraphTarget(EngineObject Owner, GraphModel Root, GraphModel Graph, Element? Element);

internal static class NodeGraphLocator
{
    public static NodeGraphTarget Locate(Scene scene, string? graphId)
    {
        if (string.IsNullOrWhiteSpace(graphId) || !Guid.TryParse(graphId, out Guid id))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"'{graphId}' is not an Id.",
                "graphId",
                "Pass the Id of a NodeGraphFilterEffect or NodeGraphDrawable; read_node_graph without graphId lists them."));
        }

        ICoreObject? found = scene.FindById(id);
        if (GraphOf(found) is not { } graph || FindOwner(graph) is not { } owner || RootOf(owner) is not { } root)
        {
            throw new ReconcileException(new ToolError(
                found is null ? ErrorCode.StaleHandle : ErrorCode.ValidationRejected,
                found is null
                    ? $"The scene has no object with Id '{graphId}'."
                    : $"'{graphId}' is a {found.GetType().Name}, which holds no node graph.",
                "graphId",
                "Pass the Id of a NodeGraphFilterEffect, a NodeGraphDrawable, or a group or loop node inside one; read_node_graph without graphId lists the graphs."));
        }

        return new NodeGraphTarget(owner, root, graph, ((IHierarchical)owner).FindHierarchicalParent<Element>());
    }

    public static IReadOnlyList<NodeGraphSummary> List(Scene scene)
    {
        var graphs = new List<NodeGraphSummary>();
        foreach ((EngineObject owner, GraphModel root) in EnumerateRoots(scene))
        {
            Element? element = ((IHierarchical)owner).FindHierarchicalParent<Element>();
            graphs.Add(new NodeGraphSummary(
                owner.Id.Value.ToString(),
                owner.GetType().Name,
                string.IsNullOrEmpty(owner.Name.Value) ? null : owner.Name.Value,
                element?.Id.Value.ToString(),
                element?.Name.Value,
                root.Nodes.Count));
        }

        return graphs;
    }

    // Every effect and drawable of the scene that holds a graph, with its graph.
    public static IEnumerable<(EngineObject Owner, GraphModel Root)> EnumerateRoots(Scene scene)
    {
        foreach (object item in ChildObjects.Walk(scene))
        {
            if (item is EngineObject owner && RootOf(owner) is { } root)
            {
                yield return (owner, root);
            }
        }
    }

    // The graph an object holds or is: an effect's or drawable's model, a group's inner graph, or the graph itself.
    private static GraphModel? GraphOf(ICoreObject? obj) => obj switch
    {
        GroupNode group => group.Group.Value,
        GraphModel graph => graph,
        EngineObject owner => RootOf(owner),
        _ => null,
    };

    private static GraphModel? RootOf(EngineObject owner) => owner switch
    {
        NodeGraphFilterEffect effect => effect.Model.Value,
        NodeGraphDrawable drawable => drawable.Model.Value,
        _ => null,
    };

    private static EngineObject? FindOwner(GraphModel graph)
        => graph.EnumerateAncestors<EngineObject>().FirstOrDefault(owner => RootOf(owner) is not null);
}

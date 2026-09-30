using System.Runtime.ExceptionServices;
using Beutl.Composition;
using Beutl.Language;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.NodeGraph.Generative;

/// <summary>What the runner needs from the editor it runs in.</summary>
public interface IGenerativeRunHost
{
    /// <summary>The time the graph is evaluated at when inputs are captured.</summary>
    TimeSpan CurrentTime { get; }

    /// <summary>Runs graph evaluation where rendering is allowed to happen.</summary>
    Task<T> InvokeOnRenderThreadAsync<T>(Func<T> func);

    /// <summary>Runs model edits where the project is allowed to change.</summary>
    Task InvokeOnUIThreadAsync(Action action);

    /// <summary>Records the edits made since the last commit as one undoable step.</summary>
    void CommitHistory(string name);
}

/// <summary>
/// Runs the generative nodes of a graph like a ComfyUI queue: upstream first, each
/// node against inputs captured after its dependencies finished, and only the nodes
/// whose complete request differs from their active generation.
/// </summary>
public sealed class GenerativeGraphRunner(IGenerativeNodeExecutor executor, IGenerativeRunHost host)
{
    /// <summary>
    /// Runs <paramref name="targets"/> (every generative node when null) together with
    /// the generative nodes they depend on. <paramref name="force"/> regenerates the
    /// targets even when their request is unchanged; dependencies are still reused.
    /// <paramref name="variations"/> above one makes that many generations of each target,
    /// as ComfyUI's batch does, and keeps them all to compare.
    /// </summary>
    public async Task RunAsync(
        GraphModel model,
        IReadOnlyCollection<GenerativeNode>? targets,
        bool force,
        CancellationToken cancellationToken,
        int variations = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(variations, 1);
        ArgumentNullException.ThrowIfNull(model);
        // A group's graph is run as part of the graph around it, which supplies its inputs.
        model = model.GetRootGraph();
        IReadOnlyList<GenerativeNode> order = PlanOrder(model, targets);
        var forced = force || variations > 1
            ? new HashSet<GenerativeNode>(targets ?? order)
            : [];
        var failed = new HashSet<GenerativeNode>();
        ExceptionDispatchInfo? unexpected = null;
        Dictionary<GraphNode, HashSet<GraphNode>> upstream = BuildUpstreamMap(model);

        await host.InvokeOnUIThreadAsync(() =>
        {
            foreach (GenerativeNode node in order)
                node.SetStatus(GenerativeNodeStatus.Queued);
        });

        for (int i = 0; i < order.Count; i++)
        {
            GenerativeNode node = order[i];
            var progress = new ProgressRelay(host, node);
            if (cancellationToken.IsCancellationRequested)
            {
                await CancelRemainingAsync(order, i);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (Ancestors(node, upstream).Any(ancestor => ancestor is GenerativeNode g && failed.Contains(g)))
            {
                failed.Add(node);
                await host.InvokeOnUIThreadAsync(() => node.SetStatus(GenerativeNodeStatus.Blocked));
                continue;
            }

            try
            {
                await host.InvokeOnUIThreadAsync(() => node.SetStatus(GenerativeNodeStatus.Running));
                if (variations > 1 && forced.Contains(node))
                {
                    await RunVariationsAsync(model, node, variations, progress, cancellationToken);
                    continue;
                }

                GenerativeRequest request = await host.InvokeOnRenderThreadAsync(() => CaptureRequest(model, node));
                if (!forced.Contains(node)
                    && node.ActiveGeneration is { } active
                    && active.Fingerprint == request.Fingerprint)
                {
                    await host.InvokeOnUIThreadAsync(() => node.SetStatus(GenerativeNodeStatus.Idle));
                    continue;
                }

                GenerativeExecutionResult result = await executor.ExecuteAsync(request, progress, cancellationToken);
                progress.Close();
                await host.InvokeOnUIThreadAsync(() =>
                {
                    GenerationRecord record = node.AddGeneration(request, result);
                    node.RenewRequestKey();
                    node.OnGenerated(record);
                    node.SetStatus(GenerativeNodeStatus.Idle);
                    host.CommitHistory(NodeGraphStrings.AiGeneration);
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                progress.Close();
                await CancelRemainingAsync(order, i);
                throw;
            }
            catch (GenerativeExecutionException ex)
            {
                progress.Close();
                failed.Add(node);
                await host.InvokeOnUIThreadAsync(() =>
                {
                    if (ex.SettledRequest)
                        node.RenewRequestKey();
                    node.RestoreActivePreview();
                    node.SetStatus(GenerativeNodeStatus.Failed, ex.Message);
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Anything else is not the node's to explain, but it must not stay running, and
                // what depends on it must not run on its old result.
                progress.Close();
                failed.Add(node);
                await host.InvokeOnUIThreadAsync(() =>
                {
                    node.RestoreActivePreview();
                    node.SetStatus(GenerativeNodeStatus.Failed);
                });
                if (unexpected is null)
                    unexpected = ExceptionDispatchInfo.Capture(ex);
            }
        }

        unexpected?.Throw();
    }

    // One generation per variation, each against a fresh capture: the key renewed after each
    // result is part of the next request. The last one stays active and the node's inputs are
    // set to reproduce it; all of them are kept to compare. Results already paid for are
    // committed even when a later variation fails or the run is stopped.
    private async Task RunVariationsAsync(
        GraphModel model,
        GenerativeNode node,
        int variations,
        ProgressRelay progress,
        CancellationToken cancellationToken)
    {
        GenerationRecord? last = null;
        GenerativeRequest? lastRequest = null;
        try
        {
            for (int index = 0; index < variations; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int captured = index;
                GenerativeRequest request = await host.InvokeOnRenderThreadAsync(
                    () => CaptureRequest(model, node, captured));
                GenerativeExecutionResult result = await executor.ExecuteAsync(request, progress, cancellationToken);
                await host.InvokeOnUIThreadAsync(() =>
                {
                    last = node.AddGeneration(request, result);
                    lastRequest = request;
                    node.RenewRequestKey();
                });
            }
        }
        finally
        {
            progress.Close();
            if (last is not null)
            {
                await host.InvokeOnUIThreadAsync(() =>
                {
                    // Seed controls move the seed on first; the kept request then sets it back.
                    node.OnGenerated(last);
                    node.ApplyRequestInputs(lastRequest!);
                    host.CommitHistory(NodeGraphStrings.AiGeneration);
                });
            }
        }

        await host.InvokeOnUIThreadAsync(() => node.SetStatus(GenerativeNodeStatus.Idle));
    }

    private async Task CancelRemainingAsync(IReadOnlyList<GenerativeNode> order, int from)
    {
        await host.InvokeOnUIThreadAsync(() =>
        {
            for (int j = from; j < order.Count; j++)
            {
                order[j].RestoreActivePreview();
                order[j].SetStatus(GenerativeNodeStatus.Canceled);
            }
        });
    }

    private GenerativeRequest CaptureRequest(GraphModel model, GenerativeNode node, int? variation = null)
    {
        // A snapshot of its own: the editor's snapshot belongs to the render loop and
        // may be rebuilt at any time. Evaluating here also picks up the generation a
        // dependency has just produced.
        var context = new CompositionContext(host.CurrentTime);
        using var snapshot = new GraphSnapshot();
        snapshot.Build(model, context);
        snapshot.Evaluate(CompositionTarget.Graphics, context);
        GraphSnapshot? owner = FindOwningSnapshot(snapshot, model, node);
        int slot = owner?.FindSlotIndex(node) ?? -1;
        if (owner?.GetResource(slot) is not { } resource || owner.GetContext(slot) is not { } nodeContext)
            throw new GenerativeExecutionException(NodeGraphStrings.Generative_Failed);
        return variation is { } index
            ? node.BuildVariation(resource, nodeContext, index)
            : node.BuildRequest(resource, nodeContext);
    }

    // A node inside groups is evaluated by its group's own snapshot, nested in the outer one.
    private static GraphSnapshot? FindOwningSnapshot(GraphSnapshot snapshot, GraphModel model, GraphNode node)
    {
        var groups = new Stack<GroupNode>();
        for (GraphModel? graph = node.FindHierarchicalParent<GraphModel>();
             graph is not null && !ReferenceEquals(graph, model);
             graph = graph.HierarchicalParent is GroupNode group ? group.FindHierarchicalParent<GraphModel>() : null)
        {
            if (graph.HierarchicalParent is not GroupNode owner)
                return null;
            groups.Push(owner);
        }

        GraphSnapshot current = snapshot;
        foreach (GroupNode group in groups)
        {
            if (current.GetResource(current.FindSlotIndex(group)) is not GroupNode.Resource { InnerSnapshot: { } inner })
                return null;
            current = inner;
        }

        return current;
    }

    /// <summary>The generative nodes to run, dependencies before dependents.</summary>
    internal static IReadOnlyList<GenerativeNode> PlanOrder(
        GraphModel model,
        IReadOnlyCollection<GenerativeNode>? targets)
    {
        Dictionary<GraphNode, HashSet<GraphNode>> upstream = BuildUpstreamMap(model);
        IEnumerable<GenerativeNode> roots = targets
            ?? model.EnumerateGraphs().SelectMany(graph => graph.Nodes).OfType<GenerativeNode>();
        var order = new List<GenerativeNode>();
        var visited = new HashSet<GraphNode>();
        var visiting = new HashSet<GraphNode>();

        void Visit(GraphNode node)
        {
            if (visited.Contains(node) || !visiting.Add(node))
                return;
            if (upstream.TryGetValue(node, out HashSet<GraphNode>? parents))
            {
                foreach (GraphNode parent in parents)
                    Visit(parent);
            }

            visiting.Remove(node);
            visited.Add(node);
            if (node is GenerativeNode generative)
                order.Add(generative);
        }

        foreach (GenerativeNode root in roots)
            Visit(root);
        return order;
    }

    private static IEnumerable<GraphNode> Ancestors(GraphNode node, Dictionary<GraphNode, HashSet<GraphNode>> upstream)
    {
        var seen = new HashSet<GraphNode>();
        var stack = new Stack<GraphNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            if (!upstream.TryGetValue(stack.Pop(), out HashSet<GraphNode>? parents))
                continue;
            foreach (GraphNode parent in parents)
            {
                if (seen.Add(parent))
                {
                    yield return parent;
                    stack.Push(parent);
                }
            }
        }
    }

    // Across groups as well: what feeds a group feeds the nodes inside it, through its input
    // node, and what the group outputs depends on the nodes inside it, through its output node.
    private static Dictionary<GraphNode, HashSet<GraphNode>> BuildUpstreamMap(GraphModel model)
    {
        var map = new Dictionary<GraphNode, HashSet<GraphNode>>();
        GraphModel[] graphs = model.EnumerateGraphs().ToArray();
        foreach (Connection connection in graphs.SelectMany(graph => graph.AllConnections))
        {
            GraphNode? input = connection.Input.Value?.FindHierarchicalParent<GraphNode>();
            GraphNode? output = connection.Output.Value?.FindHierarchicalParent<GraphNode>();
            if (input is null || output is null || ReferenceEquals(input, output))
                continue;
            ParentsOf(input).Add(output);
        }

        GroupNode[] groups = graphs.SelectMany(graph => graph.Nodes).OfType<GroupNode>().ToArray();
        foreach (GroupNode group in groups)
        {
            if (group.Group.Input is { } groupInput && map.TryGetValue(group, out HashSet<GraphNode>? outer))
                ParentsOf(groupInput).UnionWith(outer);
        }

        foreach (GroupNode group in groups)
        {
            if (group.Group.Output is { } groupOutput)
                ParentsOf(group).Add(groupOutput);
        }

        return map;

        HashSet<GraphNode> ParentsOf(GraphNode node)
        {
            if (!map.TryGetValue(node, out HashSet<GraphNode>? parents))
                map[node] = parents = [];
            return parents;
        }
    }

    // Reports are delivered later on the UI thread; once the node's run is over, those still
    // queued are dropped so they cannot replace its final preview or status.
    private sealed class ProgressRelay(IGenerativeRunHost host, GenerativeNode node) : IProgress<GenerativeProgress>
    {
        private volatile bool _closed;

        public void Close() => _closed = true;

        public void Report(GenerativeProgress value)
        {
            _ = host.InvokeOnUIThreadAsync(() =>
            {
                if (_closed)
                {
                    value.Preview?.Dispose();
                    return;
                }

                if (value.Preview is not null)
                    node.ShowPreview(value.Preview);
                if (value.Status is not null)
                    node.SetStatus(GenerativeNodeStatus.Running, value.Status);
            });
        }
    }
}

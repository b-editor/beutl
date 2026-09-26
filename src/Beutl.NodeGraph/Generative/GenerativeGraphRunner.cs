using Beutl.Composition;
using Beutl.Language;
using Beutl.NodeGraph.Composition;

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
    /// </summary>
    public async Task RunAsync(
        GraphModel model,
        IReadOnlyCollection<GenerativeNode>? targets,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlyList<GenerativeNode> order = PlanOrder(model, targets);
        var forced = force
            ? new HashSet<GenerativeNode>(targets ?? order)
            : [];
        var failed = new HashSet<GenerativeNode>();
        Dictionary<GraphNode, HashSet<GraphNode>> upstream = BuildUpstreamMap(model);

        await host.InvokeOnUIThreadAsync(() =>
        {
            foreach (GenerativeNode node in order)
                node.SetStatus(GenerativeNodeStatus.Queued);
        });

        for (int i = 0; i < order.Count; i++)
        {
            GenerativeNode node = order[i];
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
                GenerativeRequest request = await host.InvokeOnRenderThreadAsync(() => CaptureRequest(model, node));
                if (!forced.Contains(node)
                    && !node.AlwaysRun
                    && node.ActiveGeneration is { } active
                    && active.Fingerprint == request.Fingerprint)
                {
                    await host.InvokeOnUIThreadAsync(() => node.SetStatus(GenerativeNodeStatus.Idle));
                    continue;
                }

                var progress = new ProgressRelay(host, node);
                GenerativeExecutionResult result = await executor.ExecuteAsync(request, progress, cancellationToken);
                if (result.ContentFingerprint is { } produced
                    && node.ActiveGeneration?.Fingerprint == produced)
                {
                    // Redone and unchanged: keep the active result so nothing downstream reruns.
                    TryDelete(result.ResultFile);
                    await host.InvokeOnUIThreadAsync(() =>
                    {
                        node.ShowPreview(null);
                        node.SetStatus(GenerativeNodeStatus.Idle);
                    });
                    continue;
                }

                await host.InvokeOnUIThreadAsync(() =>
                {
                    GenerationRecord record = node.AddGeneration(request, result);
                    node.OnGenerated(record);
                    node.ShowPreview(null);
                    node.SetStatus(GenerativeNodeStatus.Idle);
                    host.CommitHistory(NodeGraphStrings.AiGeneration);
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CancelRemainingAsync(order, i);
                throw;
            }
            catch (GenerativeExecutionException ex)
            {
                failed.Add(node);
                await host.InvokeOnUIThreadAsync(() =>
                {
                    node.ShowPreview(null);
                    node.SetStatus(GenerativeNodeStatus.Failed, ex.Message);
                });
            }
        }
    }

    private static void TryDelete(Uri file)
    {
        try
        {
            if (file.IsFile)
                File.Delete(file.LocalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task CancelRemainingAsync(IReadOnlyList<GenerativeNode> order, int from)
    {
        await host.InvokeOnUIThreadAsync(() =>
        {
            for (int j = from; j < order.Count; j++)
            {
                order[j].ShowPreview(null);
                order[j].SetStatus(GenerativeNodeStatus.Canceled);
            }
        });
    }

    private GenerativeRequest CaptureRequest(GraphModel model, GenerativeNode node)
    {
        // A snapshot of its own: the editor's snapshot belongs to the render loop and
        // may be rebuilt at any time. Evaluating here also picks up the generation a
        // dependency has just produced.
        var context = new CompositionContext(host.CurrentTime);
        using var snapshot = new GraphSnapshot();
        snapshot.Build(model, context);
        snapshot.Evaluate(CompositionTarget.Graphics, context);
        int slot = snapshot.FindSlotIndex(node);
        if (snapshot.GetResource(slot) is not { } resource || snapshot.GetContext(slot) is not { } nodeContext)
            throw new GenerativeExecutionException(NodeGraphStrings.Generative_Failed);
        return node.BuildRequest(resource, nodeContext);
    }

    /// <summary>The generative nodes to run, dependencies before dependents.</summary>
    internal static IReadOnlyList<GenerativeNode> PlanOrder(
        GraphModel model,
        IReadOnlyCollection<GenerativeNode>? targets)
    {
        Dictionary<GraphNode, HashSet<GraphNode>> upstream = BuildUpstreamMap(model);
        IEnumerable<GenerativeNode> roots = targets ?? model.Nodes.OfType<GenerativeNode>();
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

    private static Dictionary<GraphNode, HashSet<GraphNode>> BuildUpstreamMap(GraphModel model)
    {
        var map = new Dictionary<GraphNode, HashSet<GraphNode>>();
        foreach (Connection connection in model.AllConnections)
        {
            GraphNode? input = connection.Input.Value?.FindHierarchicalParent<GraphNode>();
            GraphNode? output = connection.Output.Value?.FindHierarchicalParent<GraphNode>();
            if (input is null || output is null || ReferenceEquals(input, output))
                continue;
            if (!map.TryGetValue(input, out HashSet<GraphNode>? parents))
                map[input] = parents = [];
            parents.Add(output);
        }

        return map;
    }

    private sealed class ProgressRelay(IGenerativeRunHost host, GenerativeNode node) : IProgress<GenerativeProgress>
    {
        public void Report(GenerativeProgress value)
        {
            _ = host.InvokeOnUIThreadAsync(() =>
            {
                if (value.Preview is not null)
                    node.ShowPreview(value.Preview);
                if (value.Status is not null)
                    node.SetStatus(GenerativeNodeStatus.Running, value.Status);
            });
        }
    }
}

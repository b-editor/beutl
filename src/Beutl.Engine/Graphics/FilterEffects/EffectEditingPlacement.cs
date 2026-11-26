using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

// Captures one preview render. Read the evaluated graph's picture path, then use the matrices its filters
// actually applied. A filter's origin can depend on the bounds produced by an upstream warp at execution time.
internal sealed class EffectEditingPlacement(FilterEffect stage, EngineObject graph) : IDisposable
{
    [ThreadStatic]
    private static EffectEditingPlacement? s_current;

    private readonly EngineObject _graph = graph;
    private readonly EffectEditingPlacement? _previous = s_current;
    private readonly List<(Matrix Matrix, TransformCapture? Capture)> _steps = [];
    private readonly Dictionary<FilterEffect.Resource, TransformCapture> _captures = new(ReferenceEqualityComparer.Instance);
    private FilterEffect.Resource? _stageResource;
    private bool _observedGraph;
    private bool _hasPlacement;

    public static EffectEditingPlacement Begin(FilterEffect stage, EngineObject graph)
    {
        var placement = new EffectEditingPlacement(stage, graph);
        s_current = placement;
        return placement;
    }

    public static void ObserveGraphOutputs(EngineObject graph, IReadOnlyList<RenderNode> outputs)
    {
        if (s_current is { } placement && ReferenceEquals(graph, placement._graph))
            placement.ObserveOutputs(outputs);
    }

    public static TransformCapture? FindCapture(FilterEffect.Resource effect)
        => s_current is { _hasPlacement: true } placement ? placement._captures.GetValueOrDefault(effect) : null;

    public static void ObserveFilter(FilterEffect.Resource effect, IReadOnlyList<IFEItem> items, RenderNodeContext context)
    {
        if (s_current is not { _hasPlacement: true } placement) return;

        var capture = placement._captures.GetValueOrDefault(effect);
        if (capture is not null && items.All(static item => item.KeepsPlacement))
        {
            capture.Record(Matrix.Identity);
        }
        else if (items.Count > 0 && (capture is not null || ReferenceEquals(effect, placement._stageResource)))
        {
            // The selected effect's bounds and downstream matrices can both be resolved during execution.
            context.DisableRenderCache();
        }
    }

    public Matrix? GetTransform()
    {
        if (!_hasPlacement) return null;

        Matrix result = Matrix.Identity;
        foreach (var (matrix, capture) in _steps)
        {
            if (capture is null)
                result = matrix * result;
            else if (capture.Transform is { } applied)
                result = applied * result;
            else
                return null;
        }
        return IsUsable(result) ? result : null;
    }

    private void ObserveOutputs(IReadOnlyList<RenderNode> outputs)
    {
        if (_observedGraph)
        {
            _hasPlacement = false;
            return;
        }
        _observedGraph = true;

        // A shared subtree can reach the output more than once. Memoize the paths, and accept only a single
        // placement of the stage. The path runs from the graph output back to the effect being edited.
        var routes = new Dictionary<RenderNode, (int Count, RenderNode? Next)>(ReferenceEqualityComparer.Instance);
        RenderNode? root = null;
        foreach (RenderNode output in outputs)
        {
            int count = CountRoutes(output);
            if (count == 0) continue;
            if (root is not null || count > 1) return;
            root = output;
        }
        if (root is null) return;

        for (RenderNode current = root; routes[current].Next is { } next; current = next)
        {
            switch (current)
            {
                case ReferencesChildRenderNode:
                    break;
                case TransformRenderNode { TransformOperator: TransformOperator.Prepend } transform
                    when IsUsable(transform.Transform):
                    _steps.Add((transform.Transform, null));
                    break;
                case FilterEffectRenderNode filter when filter.GetType() == typeof(FilterEffectRenderNode):
                    if (filter.FilterEffect is { Resource: { IsEnabled: true } resource })
                    {
                        var capture = new TransformCapture();
                        if (!_captures.TryAdd(resource, capture)) return;
                        _steps.Add((default, capture));
                    }
                    break;
                default:
                    return;
            }
        }
        _hasPlacement = true;

        int CountRoutes(RenderNode node)
        {
            if (routes.TryGetValue(node, out var route)) return route.Count;
            routes.Add(node, default);

            if (node.IsDisposed) return 0;
            if (node is FilterEffectRenderNode { FilterEffect.Resource: { } resource }
                && ReferenceEquals(resource.GetOriginal(), stage))
            {
                _stageResource = resource;
                routes[node] = (1, null);
                return 1;
            }

            foreach (RenderNode child in node.ChildNodes)
            {
                int count = CountRoutes(child);
                if (count == 0) continue;
                if (count > 1 || route.Count > 0)
                {
                    route = (2, null);
                    break;
                }
                route = (1, child);
            }
            routes[node] = route;
            return route.Count;
        }
    }

    private static bool IsUsable(Matrix matrix)
        => !matrix.ContainsPerspective()
            && IsFinite(matrix)
            && matrix.TryInvert(out Matrix inverse)
            && IsFinite(inverse);

    private static bool IsFinite(Matrix matrix)
        => float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M33)
            && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22)
            && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32);

    public void Dispose() => s_current = _previous;

    internal sealed class TransformCapture
    {
        private Matrix? _first;
        private bool _ambiguous;

        public Matrix? Transform => _ambiguous ? null : _first;

        // A per-target filter can apply different origins to separate targets. In that case there is no
        // single matrix for this effect's handles.
        public void Record(Matrix matrix)
        {
            _ambiguous |= !IsUsable(matrix) || (_first is { } first && first != matrix);
            _first ??= matrix;
        }
    }
}

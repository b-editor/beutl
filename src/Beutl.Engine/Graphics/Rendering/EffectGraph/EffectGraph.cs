using Beutl.Graphics.Effects;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// One node in an <see cref="EffectGraph"/>: a descriptor plus the logical bounds it maps from and to
/// (feature 004, data-model §3). Bounds are threaded by the builder at describe time (fresh every frame) and
/// carried here so the compiler and the per-frame resource resolution can size intermediates without
/// re-walking the descriptors. Linear chains are the common case; <see cref="InputIndices"/> keeps room for
/// the branching split/composite topology that lands in a later step.
/// </summary>
internal sealed class EffectNode(
    EffectNodeDescriptor descriptor, Rect inputBounds, Rect outputBounds, int childIndex,
    NestedGraphNodePlanCache? nestedPlanCache, CustomRenderNodePlanCache? customNodeCache)
{
    public EffectNodeDescriptor Descriptor { get; } = descriptor;

    public Rect InputBounds { get; } = inputBounds;

    public Rect OutputBounds { get; } = outputBounds;

    /// <summary>
    /// The top-level group-child index this node was described under (feature 004, C10 provenance): the compiler
    /// stamps each <see cref="CompiledPass"/> with the min..max child index of the nodes it spans so the pass-prefix
    /// output cache can map a stable leading run of children to a leading run of passes. Ungrouped effects and every
    /// node inside a single (non-group) effect are child <c>0</c>; a nested group inherits its outer child index.
    /// </summary>
    public int ChildIndex { get; } = childIndex;

    /// <summary>Persistent branch-plan cache for a nested node; null for every other descriptor kind.</summary>
    public NestedGraphNodePlanCache? NestedPlanCache { get; } = nestedPlanCache;

    /// <summary>Persistent render-node cache for a custom node; null for every other descriptor kind.</summary>
    public CustomRenderNodePlanCache? CustomNodeCache { get; } = customNodeCache;

    public IReadOnlyList<int> InputIndices { get; init; } = [];
}

/// <summary>
/// The DAG an <see cref="Effects.EffectGraphBuilder"/> produces (feature 004, data-model §3): an ordered node
/// list plus the render-request context (bounds/scales) the compiler and executor need. It owns per-frame
/// describe-time disposables (sampler/child shaders), released by <see cref="Dispose"/> once the frame's plan has
/// executed. A standalone builder also transfers its private hierarchical runtime cache here; production plan render
/// nodes instead supply and own their persistent cache explicitly.
/// </summary>
internal sealed class EffectGraph(
    IReadOnlyList<EffectNode> nodes,
    Rect originalBounds,
    float outputScale,
    float workingScale,
    IReadOnlyCollection<IDisposable> disposables) : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<EffectGraph>();
    private bool _disposed;

    public IReadOnlyList<EffectNode> Nodes { get; } = nodes;

    public Rect OriginalBounds { get; } = originalBounds;

    public float OutputScale { get; } = outputScale;

    public float WorkingScale { get; } = workingScale;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (IDisposable disposable in disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                // Best effort: a broken native wrapper must not strand the rest of the graph-owned resources or
                // replace a successfully executed frame with a cleanup failure.
                s_logger.LogWarning(ex, "Effect graph disposable threw during cleanup");
            }
        }
    }
}

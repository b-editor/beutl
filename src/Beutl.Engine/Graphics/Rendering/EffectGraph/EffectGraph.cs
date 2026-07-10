using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// One node in an <see cref="EffectGraph"/>: a descriptor plus the logical bounds it maps from and to
/// (feature 004, data-model §3). Bounds are threaded by the builder at describe time (fresh every frame) and
/// carried here so the compiler and the per-frame resource resolution can size intermediates without
/// re-walking the descriptors. Linear chains are the common case; <see cref="InputIndices"/> keeps room for
/// the branching split/composite topology that lands in a later step.
/// </summary>
internal sealed class EffectNode(
    EffectNodeDescriptor descriptor, Rect inputBounds, Rect outputBounds, int childIndex)
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

    public IReadOnlyList<int> InputIndices { get; init; } = [];
}

/// <summary>
/// The DAG an <see cref="Effects.EffectGraphBuilder"/> produces (feature 004, data-model §3): an ordered node
/// list plus the render-request context (bounds/scales) the compiler and executor need. Owns no GPU state; it
/// does own per-frame describe-time disposables (sampler/child shaders), released by <see cref="Dispose"/> once
/// the frame's plan has executed.
/// </summary>
internal sealed class EffectGraph(
    IReadOnlyList<EffectNode> nodes,
    Rect originalBounds,
    float outputScale,
    float workingScale,
    IReadOnlyCollection<IDisposable> disposables) : IDisposable
{
    public IReadOnlyList<EffectNode> Nodes { get; } = nodes;

    public Rect OriginalBounds { get; } = originalBounds;

    public float OutputScale { get; } = outputScale;

    public float WorkingScale { get; } = workingScale;

    public void Dispose()
    {
        foreach (IDisposable disposable in disposables)
        {
            disposable.Dispose();
        }
    }
}

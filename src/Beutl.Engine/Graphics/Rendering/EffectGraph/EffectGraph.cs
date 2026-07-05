using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// One node in an <see cref="EffectGraph"/>: a descriptor plus the logical bounds it maps from and to
/// (feature 004, data-model §3). Bounds are threaded by the builder at describe time (fresh every frame) and
/// carried here so the compiler and the per-frame resource resolution can size intermediates without
/// re-walking the descriptors. Linear chains are the common case; <see cref="InputIndices"/> keeps room for
/// the branching split/composite topology that lands in a later step.
/// </summary>
internal sealed class EffectNode(EffectNodeDescriptor descriptor, Rect inputBounds, Rect outputBounds)
{
    public EffectNodeDescriptor Descriptor { get; } = descriptor;

    public Rect InputBounds { get; } = inputBounds;

    public Rect OutputBounds { get; } = outputBounds;

    public IReadOnlyList<int> InputIndices { get; init; } = [];
}

/// <summary>
/// The DAG an <see cref="Effects.EffectGraphBuilder"/> produces (feature 004, data-model §3): an ordered node
/// list plus the render-request context (bounds/scales) the compiler and executor need. Owns no GPU state; it
/// does own the disposables the bridge captured (an <see cref="FilterEffectContext"/> per opaque node), released
/// by <see cref="Dispose"/> once the frame's plan has executed.
/// </summary>
internal sealed class EffectGraph(
    IReadOnlyList<EffectNode> nodes,
    Rect originalBounds,
    float outputScale,
    float workingScale,
    IReadOnlyList<IDisposable> disposables) : IDisposable
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

using System.Collections.Immutable;

namespace Beutl.Graphics.Effects;

/// <summary>
/// The fan-out sink the executor hands a <see cref="SplitNodeDescriptor.Render"/> callback (feature 004,
/// data-model §1, contract A2). The callback reads its input via <see cref="Input"/> and calls <see cref="Emit"/>
/// once per branch; the executor allocates each branch's pooled output target, opens a <see cref="GeometrySession"/>
/// over it, runs the branch's draw callback, and tracks the branch for in-frame release. A static split emits
/// exactly its structural branch count; a dynamic split (<see cref="SplitNodeDescriptor.IsDynamicOutputs"/>) emits
/// an execution-time-resolved count that the executor counts and leak-checks, exempt from the static peak-live bound.
/// </summary>
public interface ISplitEmitter
{
    /// <summary>The materialized input being split.</summary>
    EffectInput Input { get; }

    /// <summary>The working density <c>w</c> resolved for the split's input.</summary>
    float WorkingScale { get; }

    /// <summary>
    /// Emits one branch occupying <paramref name="logicalBounds"/>, drawn by <paramref name="render"/>. The
    /// executor sizes and clears the branch's pooled buffer, opens a session over it, and invokes the callback.
    /// </summary>
    void Emit(Rect logicalBounds, Action<GeometrySession> render);
}

/// <summary>
/// A fan-out node (feature 004, data-model §1, contract A2, research D7): one input becomes N branch outputs,
/// each a pooled target the executor allocates. Fusion never crosses a split. <see cref="BranchCount"/> is
/// <b>structural</b> for a static split (changing it recompiles, C3.6); a dynamic split declares
/// <see cref="IsDynamicOutputs"/> and resolves its output count at execution time (contour-based part splitting).
/// The <see cref="Render"/> callback drives <see cref="ISplitEmitter.Emit"/>.
/// </summary>
public sealed record SplitNodeDescriptor : EffectNodeDescriptor
{
    private SplitNodeDescriptor(
        Action<ISplitEmitter> render, int branchCount, bool isDynamicOutputs, object structuralToken)
    {
        Render = render;
        BranchCount = branchCount;
        IsDynamicOutputs = isDynamicOutputs;
        StructuralToken = structuralToken;
    }

    /// <summary>The callback that emits the branches through the executor-provided sink.</summary>
    public Action<ISplitEmitter> Render { get; }

    /// <summary>Structural branch count for a static split; <c>0</c> for a dynamic split.</summary>
    public int BranchCount { get; }

    /// <summary>True when the branch count is resolved at execution time (dynamic outputs, C3.5).</summary>
    public bool IsDynamicOutputs { get; }

    /// <summary>Identity of the split <em>kind</em> for the structural key (paired with <see cref="BranchCount"/>).</summary>
    public object StructuralToken { get; }

    /// <summary>The split reshapes the operation set, so it lays out at execution time.</summary>
    public override BoundsContract Bounds => BoundsContract.RenderTime;

    /// <inheritdoc/>
    public override bool IsCoordinateInvariant => false;

    /// <summary>Builds a static split of exactly <paramref name="branchCount"/> branches.</summary>
    public static SplitNodeDescriptor Static(
        Action<ISplitEmitter> render, int branchCount, object? structuralToken = null)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentOutOfRangeException.ThrowIfLessThan(branchCount, 1);
        return new SplitNodeDescriptor(
            render, branchCount, isDynamicOutputs: false, structuralToken ?? render.Method.MethodHandle.Value);
    }

    /// <summary>Builds a dynamic split whose branch count is discovered at execution time (contour-based).</summary>
    public static SplitNodeDescriptor Dynamic(Action<ISplitEmitter> render, object? structuralToken = null)
    {
        ArgumentNullException.ThrowIfNull(render);
        return new SplitNodeDescriptor(
            render, branchCount: 0, isDynamicOutputs: true, structuralToken ?? render.Method.MethodHandle.Value);
    }
}

/// <summary>
/// A fan-in node (feature 004, data-model §1, contract A2): the current branch set is composited back into one
/// output by drawing each branch (at its per-input offset) under <see cref="BlendMode"/>. Fusion never crosses a
/// composite. The blend mode and branch count are structural.
/// </summary>
public sealed record CompositeNodeDescriptor : EffectNodeDescriptor
{
    private CompositeNodeDescriptor(BlendMode blendMode, ImmutableArray<Point> inputOffsets, object structuralToken)
    {
        BlendMode = blendMode;
        InputOffsets = inputOffsets;
        StructuralToken = structuralToken;
    }

    /// <summary>The blend mode branches are composited under.</summary>
    public BlendMode BlendMode { get; }

    /// <summary>Per-branch logical offsets applied while compositing (empty = branches drawn at their own bounds).</summary>
    public ImmutableArray<Point> InputOffsets { get; }

    /// <summary>Identity of the composite <em>kind</em> for the structural key.</summary>
    public object StructuralToken { get; }

    /// <summary>The composite reshapes the operation set (many to one), so it lays out at execution time.</summary>
    public override BoundsContract Bounds => BoundsContract.RenderTime;

    /// <inheritdoc/>
    public override bool IsCoordinateInvariant => false;

    /// <summary>Builds a composite node from a blend mode and optional per-branch offsets.</summary>
    public static CompositeNodeDescriptor Create(
        BlendMode blendMode, IEnumerable<Point>? inputOffsets = null, object? structuralToken = null)
    {
        return new CompositeNodeDescriptor(
            blendMode, (inputOffsets ?? []).ToImmutableArray(), structuralToken ?? blendMode);
    }
}

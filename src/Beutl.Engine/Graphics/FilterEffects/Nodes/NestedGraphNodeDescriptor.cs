namespace Beutl.Graphics.Effects;

/// <summary>
/// A per-branch nested graph node (feature 004, research D8): the executor invokes <see cref="DescribeBranch"/>
/// once per current operation with the branch index, compiles the described child graph, and executes it against
/// that single operation through the same pipeline (plans, pool, counters). This is the declarative home for meta
/// effects whose child chain must be re-described per branch — e.g. <see cref="DelayAnimationEffect"/>, whose
/// child effect runs at a clock delayed by <c>delay × branchIndex</c> after an upstream split fan-out.
/// Never fused; bounds are render-time resolved (each branch's child graph lays itself out at execution).
/// </summary>
public sealed record NestedGraphNodeDescriptor : EffectNodeDescriptor
{
    internal override EffectNodeKind Kind => EffectNodeKind.NestedGraph;

    private NestedGraphNodeDescriptor(Action<EffectGraphBuilder, int> describeBranch, object structuralToken)
    {
        DescribeBranch = describeBranch;
        StructuralToken = structuralToken;
    }

    /// <summary>Describes the child graph for one branch: (builder over the branch's bounds, branch index).</summary>
    public Action<EffectGraphBuilder, int> DescribeBranch { get; }

    /// <summary>Identity of the nested-graph kind for the structural key; equal tokens share a plan shape.</summary>
    public object StructuralToken { get; }

    /// <inheritdoc/>
    public override BoundsContract Bounds => BoundsContract.RenderTime;

    /// <inheritdoc/>
    public override bool IsCoordinateInvariant => false;

    /// <summary>
    /// Builds a nested-graph node. <paramref name="structuralToken"/> defaults to the callback's method identity,
    /// so callbacks built at the same call site (differing only in captured parameters) share a structural identity.
    /// </summary>
    public static NestedGraphNodeDescriptor Create(
        Action<EffectGraphBuilder, int> describeBranch, object? structuralToken = null)
    {
        ArgumentNullException.ThrowIfNull(describeBranch);
        return new NestedGraphNodeDescriptor(
            describeBranch, structuralToken ?? describeBranch.Method.MethodHandle.Value);
    }
}

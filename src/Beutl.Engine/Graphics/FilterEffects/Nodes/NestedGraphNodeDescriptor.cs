namespace Beutl.Graphics.Effects;

/// <summary>
/// A per-branch nested graph node (feature 004, research D8): the executor invokes <see cref="DescribeBranch"/>
/// once per current operation with the branch index, compiles the described child graph, and executes it against
/// that single operation through the same pipeline (plans, pool, counters). This is the declarative home for meta
/// effects whose child chain must be re-described per branch — e.g. <see cref="DelayAnimationEffect"/>, whose
/// child effect runs at a clock delayed by <c>delay × branchIndex</c> after an upstream split fan-out.
/// Never fused; its full-frame contract prevents ROI cropping while each branch's child graph lays itself out.
/// </summary>
public sealed record NestedGraphNodeDescriptor : EffectNodeDescriptor
{
    internal override EffectNodeKind Kind => EffectNodeKind.NestedGraph;

    private NestedGraphNodeDescriptor(
        Action<EffectGraphBuilder, int> describeBranch,
        Action<IReadOnlySet<int>>? branchesCompleted,
        object structuralToken)
    {
        DescribeBranch = describeBranch;
        BranchesCompleted = branchesCompleted;
        StructuralToken = structuralToken;
    }

    /// <summary>Describes the child graph for one branch: (builder over the branch's bounds, branch index).</summary>
    public Action<EffectGraphBuilder, int> DescribeBranch { get; }

    // The executor invokes this only after every live branch has completed successfully. Descriptor authors supply
    // it through CreateStateful when they retain state keyed by stable branch ordinal.
    internal Action<IReadOnlySet<int>>? BranchesCompleted { get; }

    /// <summary>Identity of the nested-graph kind for the structural key. Tokens share a plan only when their
    /// runtime types and <see cref="object.Equals(object?)"/> values match; equality and hash code must stay stable.</summary>
    public object StructuralToken { get; }

    /// <inheritdoc/>
    public override BoundsContract Bounds => BoundsContract.FullFrame;

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
            describeBranch, branchesCompleted: null, structuralToken ?? describeBranch.Method);
    }

    /// <summary>
    /// Builds a stateful nested-graph node. After every branch in a successful pull has finished using its
    /// branch-local graph, <paramref name="branchesCompleted"/> receives the complete set of stable live branch
    /// ordinals. Authors may use that set to retire state for ordinals that disappeared. The callback is not invoked
    /// when describing, building, compiling, resolving, or executing any branch fails; if the callback itself throws,
    /// the pull fails and the executor releases every output produced by the nested pass.
    /// </summary>
    /// <param name="describeBranch">Describes the child graph for one stable branch ordinal.</param>
    /// <param name="branchesCompleted">
    /// Observes the complete live-ordinal set after all live branches have completed successfully.
    /// </param>
    /// <param name="structuralToken">
    /// Stable identity of the nested-graph kind. Defaults to <paramref name="describeBranch"/>'s method identity.
    /// </param>
    public static NestedGraphNodeDescriptor CreateStateful(
        Action<EffectGraphBuilder, int> describeBranch,
        Action<IReadOnlySet<int>> branchesCompleted,
        object? structuralToken = null)
    {
        ArgumentNullException.ThrowIfNull(describeBranch);
        ArgumentNullException.ThrowIfNull(branchesCompleted);
        return new NestedGraphNodeDescriptor(
            describeBranch, branchesCompleted, structuralToken ?? describeBranch.Method);
    }
}

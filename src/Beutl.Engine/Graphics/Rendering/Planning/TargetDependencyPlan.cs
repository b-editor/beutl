using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering;

internal sealed class TargetDependencyPlan
{
    public TargetDependencyPlan(
        ImmutableArray<TargetDependencyStep> steps,
        ImmutableArray<TargetScopePlan> scopes)
    {
        Steps = steps;
        Scopes = scopes;
    }

    public ImmutableArray<TargetDependencyStep> Steps { get; }

    public ImmutableArray<TargetScopePlan> Scopes { get; }
}

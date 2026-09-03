namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct TargetDependencyStep(
    RenderFragmentId FragmentId,
    TargetScopeId ScopeId,
    TargetTokenId InputToken,
    TargetTokenId OutputToken,
    RenderValueId? TargetReadValueId,
    RenderValueId? ProducedValueId,
    TargetDependencyKind Kind);

namespace Beutl.Graphics.Rendering;

internal readonly record struct TargetScopePlan(
    TargetScopeId Id,
    TargetScopeId? ParentId,
    RenderFragmentId? OwnerFragmentId,
    TargetTokenId InitialToken,
    Rect? ResolvedDomain,
    bool IsOrderOnly);

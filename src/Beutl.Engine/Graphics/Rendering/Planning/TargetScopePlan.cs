namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct TargetScopePlan(
    TargetScopeId Id,
    TargetScopeId? ParentId,
    RenderFragmentId? OwnerFragmentId,
    TargetTokenId InitialToken,
    Rect? ResolvedDomain,
    bool IsOrderOnly);

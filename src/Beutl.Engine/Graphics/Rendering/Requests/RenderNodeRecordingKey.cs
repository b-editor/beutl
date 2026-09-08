using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Everything about a request that a <see cref="RenderNode"/> can read while it records.
/// </summary>
/// <remarks>
/// A recording is only reusable for a request that agrees on every one of these. It carries the whole
/// observable surface of <see cref="RenderRequestOptions"/> rather than the members nodes read today, so a
/// value later exposed to <see cref="RenderNodeContext"/> or <see cref="RenderNodePreparation"/> cannot
/// silently widen what a cached recording depends on. <c>Owner</c> and the request ID are deliberately absent:
/// both are new every request and neither reaches a node.
/// </remarks>
internal readonly record struct RenderNodeRecordingKey(
    RenderIntent Intent,
    RenderRequestPurpose Purpose,
    Rect? TargetDomain,
    Rect? RequestedRegion,
    float OutputScale,
    float MaxWorkingScale,
    bool CacheEnabled,
    RenderCacheRules CacheRules,
    FusionMode FusionMode,
    bool HasSeparateTargetBinding,
    bool TransactionCacheEnabled)
{
    public static RenderNodeRecordingKey Create(
        RenderRequestOptions options,
        bool transactionCacheEnabled)
        => new(
            options.Intent,
            options.Purpose,
            options.TargetDomain,
            options.RequestedRegion,
            options.OutputScale,
            options.MaxWorkingScale,
            options.CachePolicy.IsEnabled,
            options.CachePolicy.Rules,
            options.FusionMode,
            options.TargetBinding is not null,
            transactionCacheEnabled);
}

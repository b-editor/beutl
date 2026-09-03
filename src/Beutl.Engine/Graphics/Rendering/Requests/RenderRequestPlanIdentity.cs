using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct RenderRequestPlanIdentity(
    RenderRequestPurpose Purpose,
    FusionMode FusionMode,
    bool CacheEnabled,
    RenderCacheRules CacheRules);

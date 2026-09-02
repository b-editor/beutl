namespace Beutl.Graphics.Rendering.Cache;

internal sealed record RenderNodeCachePublication(
    RenderNodeCache Cache,
    RenderOutputCacheIdentity Identity,
    IReadOnlyList<RenderNodeCachedValue> Values);

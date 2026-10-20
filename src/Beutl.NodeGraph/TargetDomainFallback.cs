using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.NodeGraph;

// Renders with the request as given and, if the graph needs a target domain, again with the context's one.
// Without a domain the RenderTargetDomainRequiredException is left to the caller.
internal static class TargetDomainFallback
{
    public static T Render<T>(
        RenderNode node,
        RenderNodeRenderRequest request,
        Rect? domain,
        Func<RenderNodeRenderer, T> run)
    {
        try
        {
            using var renderer = new RenderNodeRenderer(node, request);
            return run(renderer);
        }
        catch (RenderTargetDomainRequiredException) when (domain is { } targetDomain)
        {
            using var renderer = new RenderNodeRenderer(node, request with { TargetDomain = targetDomain });
            return run(renderer);
        }
    }
}

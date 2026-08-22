using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

internal static class RenderCacheTestSupport
{
    public static RenderOutputCacheIdentity CreateCacheIdentity(
        Rect bounds,
        string name = "test-cache",
        string device = "test-device",
        string context = "test-context")
    {
        var fragment = new RenderFragmentReference(
            RenderFragmentKind.Layer,
            bounds,
            EffectiveScale.At(1),
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            inputs: null,
            payload: null,
            hitTest: null);
        return new RenderOutputCacheIdentity(
            name,
            RenderFragmentOutputIdentity.Create(fragment, new RenderRequestId(1)),
            bounds,
            RequiredRegion.Region(bounds),
            density: 1,
            RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            FusionMode.Enabled,
            new RenderCacheDeviceContextIdentity(device, context));
    }

    public static RenderNodeCachePublication CreatePublication(
        RenderNodeCache cache,
        RenderTarget target,
        Rect bounds,
        string name = "test-cache",
        string device = "test-device",
        string context = "test-context")
    {
        return new RenderNodeCachePublication(
            cache,
            CreateCacheIdentity(bounds, name, device, context),
            [new RenderNodeCachedValue(target, bounds, EffectiveScale.At(1))]);
    }

    /// <summary>
    /// Drives a cache to the stable-request count that lets it capture, standing in for the manual
    /// render-count control the pipeline no longer exposes.
    /// </summary>
    public static void RecordStableRequests(
        this RenderNodeCache cache,
        int count = RenderNodeCache.StableRequestCount)
    {
        for (int index = 0; index < count; index++)
            cache.RecordSuccessfulStableRequest();
    }

    /// <summary>
    /// Clears the change a subtree reports from having just been built, so a fixture starts from the settled
    /// state a running renderer reaches one frame after assembling the same tree.
    /// </summary>
    /// <remarks>
    /// Attaching a child changes what its container composes, so a freshly assembled tree is dirty. A test
    /// that wants to observe cache warmup, or that pre-warms a cache directly, is not interested in that
    /// first frame.
    /// </remarks>
    public static void SettleConstruction(this RenderNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.ClearChanges(node.ChangeVersion);
        foreach (RenderNode child in node.ChildNodes.ToArray())
            child.SettleConstruction();
    }
}

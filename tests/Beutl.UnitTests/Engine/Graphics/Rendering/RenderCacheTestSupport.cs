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
}

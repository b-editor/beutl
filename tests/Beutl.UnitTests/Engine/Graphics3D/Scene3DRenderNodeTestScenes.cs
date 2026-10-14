using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics3D;

// The scene and the request the Scene3DRenderNode recording tests build.
internal static class Scene3DRenderNodeTestScenes
{
    public static Scene3D.Resource CreateScene(Rect bounds)
    {
        var scene = new Scene3D();
        // An opaque background answers over the whole scene, so the controls do not depend on geometry.
        scene.BackgroundColor.CurrentValue = Colors.Black;
        scene.RenderWidth.CurrentValue = (float)bounds.Width;
        scene.RenderHeight.CurrentValue = (float)bounds.Height;
        return (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
    }

    public static RenderNodeRenderRequest CreateRequest(
        Rect targetDomain,
        bool? supports3DRendering,
        Device3DExtentBudget? budget = null)
        => new()
        {
            Intent = RenderIntent.Preview,
            TargetDomain = targetDomain,
            CacheOptions = RenderCacheOptions.Disabled,
            Supports3DRendering = supports3DRendering,
            Device3DExtentBudget = budget,
        };
}

// Keeps a measured scene, its node and its renderer alive until the assertions have run.
internal sealed class RenderNodeMeasurementProbe(
    Scene3D.Resource scene,
    Scene3DRenderNode node,
    RenderNodeRenderer renderer,
    RenderNodeMeasurement measurement) : IDisposable
{
    public RenderNodeMeasurement Measurement { get; } = measurement;

    public void Dispose()
    {
        renderer.Dispose();
        node.Dispose();
        scene.Dispose();
    }
}

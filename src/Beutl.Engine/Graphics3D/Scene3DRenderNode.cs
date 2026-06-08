using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Lighting;

namespace Beutl.Graphics3D;

/// <summary>
/// Render node for 3D scene rendering.
/// </summary>
internal sealed class Scene3DRenderNode(Scene3D.Resource scene) : RenderNode
{
    public Rect Bounds { get; private set; }

    public (Scene3D.Resource Resource, int Version)? Scene { get; private set; } = scene.Capture();

    public bool Update(Scene3D.Resource scene)
    {
        bool changed = false;

        if (!scene.Compare(Scene))
        {
            Scene = scene.Capture();
            changed = true;
            Bounds = new Rect(0, 0, scene.RenderWidth, scene.RenderHeight);
        }

        HasChanges = changed;
        return changed;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        var scene = Scene?.Resource;
        if (scene == null)
            return [];

        var graphicsContext = GraphicsContextFactory.SharedContext;
        if (graphicsContext == null || !graphicsContext.Supports3DRendering)
            return [];

        // Camera is already a Resource from the source generator
        var cameraResource = scene.Camera;
        if (cameraResource == null)
            return [];

        int width = (int)scene.RenderWidth;
        int height = (int)scene.RenderHeight;

        if (width <= 0 || height <= 0)
            return [];

        // feature 003: render the 3D scene at the output density (ceil(size × s_out)) so it stays crisp
        // under supersampled export / full-scale preview rather than being upscaled by the root CTM. The two
        // axes are ceil()'d independently, so at a fractional scale the device aspect dw/dh can deviate from
        // width/height by a sub-pixel amount, nudging the camera projection (aspectRatio = dw/dh) by <~0.001;
        // the result is then resampled into the correctly-proportioned logical Bounds. w == 1 keeps the exact
        // logical-size surface + point-blit path (byte-identical).
        float w = context.OutputScale;
        int dw = w == 1f ? width : (int)MathF.Ceiling(width * w);
        int dh = w == 1f ? height : (int)MathF.Ceiling(height * w);

        // Get or create renderer
        var renderer = scene.Renderer ??= new Renderer3D(graphicsContext);

        // Initialize or resize if needed
        if (renderer.Width != dw || renderer.Height != dh)
        {
            if (renderer.Width == 0 || renderer.Height == 0)
            {
                renderer.Initialize(dw, dh);
            }

            renderer.Resize(dw, dh);
        }

        var objectResources = new List<Object3D.Resource>();
        var lightResources = new List<Light3D.Resource>();
        objectResources.AddRange(scene.Objects.Where(obj => obj.IsEnabled));
        lightResources.AddRange(scene.Lights.Where(light => light.IsEnabled));

        // Find gizmo target object
        Object3D.Resource? gizmoTarget = null;
        if (scene.GizmoTarget.HasValue)
        {
            gizmoTarget = FindObjectById(objectResources, scene.GizmoTarget.Value);
        }

        // Render
        renderer.Render(
            new CompositionContext(scene.Time)
            {
                DisableResourceShare = scene.DisableResourceShare,
            },
            cameraResource,
            objectResources,
            lightResources,
            scene.BackgroundColor,
            scene.AmbientColor,
            scene.AmbientIntensity,
            gizmoTarget,
            scene.GizmoMode);

        // Get the rendered surface
        var surface = renderer.CreateSkiaSurface();
        if (surface == null)
            return [];

        // Create the render operation that will draw the 3D scene. The surface is a concrete bitmap at the
        // working density w (it is not vector / re-rasterizable), so tag it At(w) honestly — including w == 1,
        // where the At(1) tag still takes the point-blit branch downstream (Value == 1f) but now reports the
        // surface's true density so a consumer caps its working scale at the rendered resolution.
        var operation = RenderNodeOperation.CreateFromSurface(
            Bounds,
            new Point(0, 0),
            surface,
            EffectiveScale.At(w));

        return [operation];
    }

    private static Object3D.Resource? FindObjectById(IEnumerable<Object3D.Resource> objects, Guid targetId)
    {
        foreach (var obj in objects)
        {
            if (obj.GetOriginal()?.Id == targetId)
                return obj;

            // Recursively search children
            var children = obj.GetChildResources();
            var found = FindObjectById(children, targetId);
            if (found != null)
                return found;
        }

        return null;
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Scene = null;
    }
}

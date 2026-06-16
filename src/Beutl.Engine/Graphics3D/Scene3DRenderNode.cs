using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Lighting;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics3D;

/// <summary>
/// Render node for 3D scene rendering.
/// </summary>
internal sealed class Scene3DRenderNode(Scene3D.Resource scene) : RenderNode
{
    private static readonly ILogger s_logger = Log.CreateLogger<Scene3DRenderNode>();

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

        // Render the 3D scene at output density. The 3D projection matrix is adjusted to
        // compensate so that logical coordinates remain unchanged despite the dense surface.
        float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, width, height), context.OutputScale);
        int dw = w == 1f ? width : (int)MathF.Ceiling(width * w);
        int dh = w == 1f ? height : (int)MathF.Ceiling(height * w);

        var renderer = scene.Renderer ??= new Renderer3D(graphicsContext);

        // Catch allocation failures (e.g. vkCreateImage past GPU limit) and drop the 3D op.
        if (renderer.Width != dw || renderer.Height != dh)
        {
            try
            {
                if (renderer.Width == 0 || renderer.Height == 0)
                {
                    renderer.Initialize(dw, dh);
                }

                renderer.Resize(dw, dh);
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex,
                    "3D render surface allocation failed ({Width}x{Height} px, density {Scale}); dropping the 3D op for this frame.",
                    dw, dh, w);
                // Failed resize may leave the renderer inconsistent; discard so next frame rebuilds.
                scene.Renderer?.Dispose();
                scene.Renderer = null;
                return [];
            }
        }

        renderer.SurfaceDensity = w;

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

        // Tag the concrete bitmap surface at its rendered density At(w).
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

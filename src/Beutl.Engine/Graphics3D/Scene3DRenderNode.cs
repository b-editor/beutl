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

        // feature 003: render the 3D scene at the output density (ceil(size × s_out)) so it stays crisp under
        // supersampled export / full-scale preview instead of being upscaled by the root CTM. The two axes are
        // ceil()'d independently, so at a fractional scale the device aspect dw/dh can deviate from width/height
        // by a sub-pixel amount, nudging the camera projection (aspectRatio = dw/dh) by <~0.001; the result is
        // resampled into the correctly-proportioned logical Bounds. w == 1 keeps the exact logical-size surface
        // + point-blit path (byte-identical).
        // Clamp the density so the device surface stays allocatable (FR-037(b)): this is the one buffer-allocating
        // boundary that takes a raw OutputScale, and Renderer3D → VulkanContext.CreateTexture2D throws on
        // vkCreateImage past the GPU axis limit (an 8192-wide scene under 4× export sizes 32768 px and crashes).
        // Mirror the 2D intermediates: clamp w, recompute the surface, and record the clamped density for the
        // renderer's logical→device hit-test math.
        // EffectiveScale.At(w) below never throws because w is the OutputScale (sanitized positive-finite by
        // RenderNodeContext) through ClampWorkingScaleToBufferBudget, which preserves finite-positive; a future w
        // from another density must guard it (EffectiveScale.AtOrUnbounded).
        float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, width, height), context.OutputScale);
        int dw = w == 1f ? width : (int)MathF.Ceiling(width * w);
        int dh = w == 1f ? height : (int)MathF.Ceiling(height * w);

        // Get or create renderer
        var renderer = scene.Renderer ??= new Renderer3D(graphicsContext);

        // Initialize or resize if needed. feature 003 (FR-037(b)): unlike the 2D sinks (which allocate through
        // RenderTarget.Create's try/catch and degrade to null on an over-limit size), Renderer3D goes straight to
        // VulkanContext.CreateTexture2D, which THROWS on vkCreateImage past the GPU axis limit. dw/dh are already
        // clamped to MaxBufferDimension (16384), but a backend whose real limit is LOWER (e.g. mobile/embedded
        // Vulkan reporting 8192) would still throw, and an uncaught throw on the render thread crashes the whole
        // render. Catch it and drop just the 3D op instead, mirroring the 2D degrade. The complete fix (a follow-up)
        // is to query the backend's true maxImageDimension2D and feed it to the clamp.
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
                return [];
            }
        }

        // Record the density so the renderer's hit-test entry points (which take LOGICAL coordinates)
        // can convert into this device-px surface — see Renderer3D.SurfaceDensity.
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

        // Create the render operation that will draw the 3D scene. The surface is a concrete bitmap at the working
        // density w (not vector / re-rasterizable), so tag it At(w) — including w == 1, where At(1) still takes the
        // point-blit branch downstream (Value == 1f) yet reports the true density so a consumer caps its working
        // scale at the rendered resolution.
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

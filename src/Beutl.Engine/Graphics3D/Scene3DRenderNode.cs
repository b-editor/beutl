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

        // Render the 3D scene at the resolved output density. The 3D projection matrix is adjusted to
        // compensate so that logical coordinates remain unchanged despite the dense surface.
        float resolved = RenderNodeContext.ResolveWorkingScale([], context.OutputScale, context.MaxWorkingScale);
        float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, width, height), resolved);
        int dw = w == 1f ? width : (int)MathF.Ceiling(width * w);
        int dh = w == 1f ? height : (int)MathF.Ceiling(height * w);

        Renderer3D? auxiliaryRenderer = context.IsAuxiliaryPull
            ? new Renderer3D(graphicsContext)
            : null;
        Renderer3D renderer = auxiliaryRenderer ?? (scene.Renderer ??= new Renderer3D(graphicsContext));

        // Preview may drop allocation failures; delivery must surface them so exports cannot silently lose 3D content.
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
                    "3D render surface allocation failed ({Width}x{Height} px, density {Scale}, intent {RenderIntent}).",
                    dw, dh, w, context.RenderIntent);
                // Failed resize may leave the renderer inconsistent. An auxiliary renderer is isolated from the
                // retained frame renderer; a failed frame renderer is discarded so the next frame rebuilds it.
                renderer.Dispose();
                if (auxiliaryRenderer == null)
                    scene.Renderer = null;
                if (context.RenderIntent == RenderIntent.Delivery)
                {
                    throw new InvalidOperationException(
                        $"3D render surface allocation failed ({dw}x{dh} px, density {w}).", ex);
                }

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
        SkiaSharp.SKSurface? surface;
        try
        {
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
                context.RenderIntent,
                context.PullPurpose,
                gizmoTarget,
                scene.GizmoMode);

            surface = renderer.CreateSkiaSurface();
        }
        catch
        {
            auxiliaryRenderer?.Dispose();
            throw;
        }

        if (surface == null)
        {
            auxiliaryRenderer?.Dispose();
            if (context.RenderIntent == RenderIntent.Delivery)
            {
                throw new InvalidOperationException(
                    $"Could not create the 3D output surface ({dw}x{dh} px, density {w}).");
            }

            return [];
        }

        // Tag the concrete bitmap surface at its rendered density At(w).
        var operation = RenderNodeOperation.CreateFromSurface(
            Bounds,
            new Point(0, 0),
            surface,
            EffectiveScale.At(w));

        if (auxiliaryRenderer == null)
            return [operation];

        // The Skia surface wraps the temporary renderer's output texture. Keep the renderer alive through deferred
        // operation execution, then dispose the surface first and the renderer second.
        return
        [
            RenderNodeOperation.CreateDecorator(
                operation, operation.Render, onDispose: auxiliaryRenderer.Dispose)
        ];
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

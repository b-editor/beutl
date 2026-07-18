using System.Runtime.ExceptionServices;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Lighting;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics3D;

/// <summary>
/// Render node for 3D scene rendering.
/// </summary>
internal sealed class Scene3DRenderNode : RenderNode
{
    private static readonly ILogger s_logger = Log.CreateLogger<Scene3DRenderNode>();

    [ThreadStatic]
    private static Func<IGraphicsContext?>? s_graphicsContextProviderForTest;

    [ThreadStatic]
    private static Action? s_afterAuxiliaryCopyCreatedForTest;

    [ThreadStatic]
    private static Action<IDisposable>? s_auxiliaryDisposerForTest;

    public Scene3DRenderNode(Scene3D.Resource scene)
    {
        using Scene3D.Resource.RenderOperationLease resourceOperation = scene.BeginRenderOperation();
        Bounds = new Rect(0, 0, scene.RenderWidth, scene.RenderHeight);
        Scene = scene.Capture();
    }

    public Rect Bounds { get; private set; }

    public (Scene3D.Resource Resource, int Version)? Scene { get; private set; }

    public bool Update(Scene3D.Resource scene)
    {
        using Scene3D.Resource.RenderOperationLease operation = scene.BeginRenderOperation();
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

        using Scene3D.Resource.RenderOperationLease resourceOperation = scene.BeginRenderOperation();

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

        var graphicsContext = s_graphicsContextProviderForTest?.Invoke() ?? GraphicsContextFactory.SharedContext;
        if (graphicsContext == null || !graphicsContext.Supports3DRendering)
            return [];

        if (context.IsAuxiliaryPull)
        {
            // Auxiliary pulls feed real pixels (brush previews, node-graph thumbnails, headless stills), so they
            // render the scene like a frame pull — through a transient renderer, because the shared frame renderer's
            // surface size and cached hit-test state must not be disturbed by an out-of-band pull at another density.
            return RenderAuxiliary(scene, cameraResource, graphicsContext, context, dw, dh, w);
        }

        Renderer3D renderer = scene.Renderer ??= new Renderer3D(graphicsContext);

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
                // Failed resize may leave the renderer inconsistent, so discard it and rebuild next frame. Clear
                // the field before the fallible native teardown, and never let a teardown throw replace the
                // allocation failure (delivery) or abort the preview drop.
                scene.Renderer = null;
                DisposeRendererAfterFailure(renderer);
                if (context.RenderIntent == RenderIntent.Delivery)
                {
                    throw new InvalidOperationException(
                        $"3D render surface allocation failed ({dw}x{dh} px, density {w}).", ex);
                }

                return [];
            }
        }

        renderer.SurfaceDensity = w;

        RenderScene(renderer, scene, cameraResource, context);
        SkiaSharp.SKSurface? surface = renderer.CreateSkiaSurface();

        if (surface == null)
        {
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

        return [operation];
    }

    internal static void SetGraphicsContextProviderForTest(Func<IGraphicsContext?>? provider)
        => s_graphicsContextProviderForTest = provider;

    internal static void SetAuxiliaryCleanupHooksForTest(
        Action? afterCopyCreated, Action<IDisposable>? disposer)
    {
        s_afterAuxiliaryCopyCreatedForTest = afterCopyCreated;
        s_auxiliaryDisposerForTest = disposer;
    }

    private static void DisposeAuxiliaryResource(IDisposable resource)
    {
        if (s_auxiliaryDisposerForTest is { } disposer)
        {
            disposer(resource);
        }
        else
        {
            resource.Dispose();
        }
    }

    // Native pass teardown is itself fallible; a discard-and-rebuild path must log a teardown throw instead of
    // letting it replace the primary failure or abort a preview drop.
    private static void DisposeRendererAfterFailure(Renderer3D renderer)
    {
        try
        {
            renderer.Dispose();
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "3D renderer teardown failed while discarding an inconsistent renderer.");
        }
    }

    private static void RenderScene(
        Renderer3D renderer, Scene3D.Resource scene, Camera3D.Resource camera, RenderNodeContext context)
    {
        var objectResources = new List<Object3D.Resource>();
        var lightResources = new List<Light3D.Resource>();
        objectResources.AddRange(scene.Objects.Where(obj => obj.IsEnabled));
        lightResources.AddRange(scene.Lights.Where(light => light.IsEnabled));

        Object3D.Resource? gizmoTarget = null;
        if (scene.GizmoTarget.HasValue)
        {
            gizmoTarget = FindObjectById(objectResources, scene.GizmoTarget.Value);
        }

        renderer.Render(
            CreateCompositionContextCore(scene, context),
            camera,
            objectResources,
            lightResources,
            scene.BackgroundColor,
            scene.AmbientColor,
            scene.AmbientIntensity,
            context.RenderIntent,
            context.PullPurpose,
            gizmoTarget,
            scene.GizmoMode);
    }

    internal static CompositionContext CreateCompositionContext(
        Scene3D.Resource scene,
        RenderNodeContext context)
    {
        using Scene3D.Resource.RenderOperationLease operation = scene.BeginRenderOperation();
        return CreateCompositionContextCore(scene, context);
    }

    private static CompositionContext CreateCompositionContextCore(
        Scene3D.Resource scene,
        RenderNodeContext context)
    {
        return new CompositionContext(
            scene.Time,
            context.RenderIntent,
            context.PullPurpose)
        {
            DisableResourceShare = scene.DisableResourceShare,
            PreferProxy = scene.PreferProxy,
            PreferredProxyPreset = scene.PreferredProxyPreset,
        };
    }

    // The auxiliary render goes through a transient renderer and hands its pixels off in a self-contained
    // RenderTarget: the emitted operation must not depend on renderer-owned GPU textures that are torn down here.
    private RenderNodeOperation[] RenderAuxiliary(
        Scene3D.Resource scene, Camera3D.Resource camera, IGraphicsContext graphicsContext,
        RenderNodeContext context, int dw, int dh, float w)
    {
        Renderer3D? renderer = null;
        SkiaSharp.SKSurface? surface = null;
        RenderTarget? copy = null;
        RenderNodeOperation? operation = null;
        bool operationFailed = false;
        try
        {
            try
            {
                renderer = new Renderer3D(graphicsContext);
                renderer.Initialize(dw, dh);
            }
            catch (Exception ex)
            {
                operationFailed = true;
                s_logger.LogWarning(ex,
                    "Auxiliary 3D render surface allocation failed ({Width}x{Height} px, density {Scale}, intent {RenderIntent}).",
                    dw, dh, w, context.RenderIntent);
                if (context.RenderIntent == RenderIntent.Delivery)
                {
                    throw new InvalidOperationException(
                        $"Auxiliary 3D render surface allocation failed ({dw}x{dh} px, density {w}).", ex);
                }

                return [EmptyAuxiliaryOperation(w)];
            }

            renderer.SurfaceDensity = w;
            RenderScene(renderer, scene, camera, context);
            surface = renderer.CreateSkiaSurface();
            if (surface == null)
            {
                operationFailed = true;
                if (context.RenderIntent == RenderIntent.Delivery)
                {
                    throw new InvalidOperationException(
                        $"Could not create the auxiliary 3D output surface ({dw}x{dh} px, density {w}).");
                }

                return [EmptyAuxiliaryOperation(w)];
            }

            copy = RenderTarget.Create(dw, dh);
            if (copy == null)
            {
                operationFailed = true;
                if (context.RenderIntent == RenderIntent.Delivery)
                {
                    throw new InvalidOperationException(
                        $"Auxiliary 3D output copy allocation failed ({dw}x{dh} px, density {w}).");
                }

                return [EmptyAuxiliaryOperation(w)];
            }

            s_afterAuxiliaryCopyCreatedForTest?.Invoke();

            using (var canvas = new ImmediateCanvas(
                copy, context.RenderIntent, w, context.MaxWorkingScale, logicalSize: Bounds.Size,
                pullPurpose: context.PullPurpose))
            {
                canvas.Clear();
                using (canvas.PushDeviceSpace())
                {
                    canvas.Canvas.DrawSurface(surface, 0, 0);
                }
            }

            operation = RenderNodeOperation.CreateFromRenderTarget(
                Bounds, new Point(0, 0), copy, EffectiveScale.At(w));
            copy = null; // ownership moved to the operation
            return [operation];
        }
        catch
        {
            operationFailed = true;
            throw;
        }
        finally
        {
            Exception? cleanupFailure = null;
            CaptureAuxiliaryDisposeFailure(copy, ref cleanupFailure);
            CaptureAuxiliaryDisposeFailure(surface, ref cleanupFailure);
            CaptureAuxiliaryDisposeFailure(renderer, ref cleanupFailure);

            if (cleanupFailure != null)
            {
                if (operationFailed)
                {
                    s_logger.LogWarning(cleanupFailure,
                        "Auxiliary 3D resources failed to dispose after a render failure.");
                }
                else
                {
                    // A return was already prepared. If teardown fails, reclaim the operation that the caller will
                    // never receive before reporting the first cleanup failure.
                    Exception firstCleanupFailure = cleanupFailure;
                    CaptureAuxiliaryDisposeFailure(operation, ref cleanupFailure);
                    ExceptionDispatchInfo.Capture(firstCleanupFailure).Throw();
                }
            }
        }
    }

    private static void CaptureAuxiliaryDisposeFailure(IDisposable? resource, ref Exception? failure)
    {
        if (resource == null)
            return;

        try
        {
            DisposeAuxiliaryResource(resource);
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }
    }

    // Preview keeps the hit-test/bounds contract alive when the auxiliary render cannot allocate.
    private RenderNodeOperation EmptyAuxiliaryOperation(float w)
        => RenderNodeOperation.CreateLambda(
            Bounds,
            static _ => { },
            Bounds.Contains,
            effectiveScale: EffectiveScale.At(w));

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

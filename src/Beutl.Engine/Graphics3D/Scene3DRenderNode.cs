using System.Runtime.ExceptionServices;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Textures;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics3D;

/// <summary>
/// Render node for 3D scene rendering.
/// </summary>
internal sealed class Scene3DRenderNode(Scene3D.Resource scene) : RenderNode
{
    private static readonly ILogger s_logger = Log.CreateLogger<Scene3DRenderNode>();

    public Rect Bounds { get; private set; } = new(0, 0, scene.RenderWidth, scene.RenderHeight);

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

        if (changed)
        {
            MarkChanged();
        }

        return changed;
    }

    public override void Process(RenderNodeContext context)
    {
        if (Scene is not { } sceneSnapshot)
            return;

        // The whole value lives on the 3D backend, so without one there is nothing to describe: a published
        // description would answer hit tests over a scene that is never drawn and, walking topmost-first,
        // swallow the clicks meant for the 2D content beneath it. This reads request state, not the
        // process - the request settled the answer before any node recorded, and it keys the recording.
        if (!context.Supports3DRendering)
            return;

        Scene3D.Resource scene = sceneSnapshot.Resource;
        Camera3D.Resource? camera = scene.Camera;
        float width = scene.RenderWidth;
        float height = scene.RenderHeight;
        if (camera is null
            || !float.IsFinite(width)
            || !float.IsFinite(height)
            || width <= 0
            || height <= 0)
        {
            return;
        }

        Rect bounds = new(0, 0, width, height);
        float workingScale = RenderScaleContract.MaterializeAtWorkingScale.Resolve(
            [],
            bounds,
            context.OutputScale,
            context.MaxWorkingScale).Value;
        // The same question Renderer3D.Initialize asks, asked while there is still somewhere to put the
        // answer. An extent the device cannot attach is refused there, and a preview drops the value rather
        // than failing; recording it anyway would publish bounds and a hit test for a value that is never
        // drawn, and walking topmost-first that hit swallows the clicks meant for the 2D content beneath.
        // Delivery keeps recording, because there the refusal is reported rather than dropped and must stay
        // so.
        //
        // This is exact for the 3D surface: RenderCore sizes it from these same origin-free bounds at a
        // density the executor resolves from the same scale contract, and its extra clamps are against the
        // engine ceiling, which can only lower a density and so can only make that allocation fit.
        //
        // It is not exact for the 2D intermediate the published output lands in. CreateOwnedValue sizes that
        // one as PixelRect.FromRect(bounds.Translate(gridOffset), density), and a fractional device-grid
        // phase makes it one pixel wider than ceil(extent x density) on each axis. The phase comes from the
        // target being drawn into, so a recording cannot know it - RenderDeviceGridSensitivity.Insensitive
        // declares that this node's pixels do not depend on the phase, not that the phase is integral. A
        // scene whose footprint lands exactly on the device's limit therefore still records while the pool
        // refuses that one-pixel-wider intermediate, and a preview drops the value with the hit answer left
        // behind. Widening this check by that pixel would instead refuse a scene at exactly the limit on
        // every device whose limit is under the engine ceiling, including the phases where it renders, so
        // the exact-boundary case is left as the residue rather than paid for with dropped content. It is
        // pinned by Scene3DRenderNodeDeviceBudgetTests.ASceneExactlyAtTheDeviceLimit_IsAKnownGap.
        (int deviceWidth, int deviceHeight) = ResolveDeviceFootprint(bounds, workingScale);
        if (context.Intent == RenderIntent.Preview
            && !CanRenderScene(context.Device3DExtentBudget, deviceWidth, deviceHeight))
        {
            if (context.Purpose == RenderRequestPurpose.Frame)
            {
                s_logger.LogWarning(
                    "A 3D scene needing a {Width}x{Height} px surface is not one this device can render "
                    + "(attachment limit {Attachment} px, cube face limit {CubeFace} px, 0 meaning "
                    + "unreported); dropping the 3D value for this preview request.",
                    deviceWidth,
                    deviceHeight,
                    context.Device3DExtentBudget.MaxAttachmentDimension,
                    context.Device3DExtentBudget.MaxCubeFaceDimension);
            }

            return;
        }

        Object3D.Resource[] objects = scene.Objects.Where(static item => item.IsEnabled).ToArray();
        // A 2D card lays its drawables out on a canvas the size of this scene, as a 2D scene that size would.
        var canvasSize = new Size(width, height);
        foreach (Object3D.Resource obj in EnumerateObjects(objects))
        {
            if (obj is DrawableObject3D.Resource card)
                card.UpdateLayout(canvasSize, workingScale);
        }

        Light3D.Resource[] lights = scene.Lights.Where(static item => item.IsEnabled).ToArray();
        Object3D.Resource? gizmoTarget = scene.GizmoTarget is { } targetId
            ? FindObjectById(objects, targetId)
            : null;
        SceneTextureBinding[] textureBindings = RecordDrawableTextures(
            context,
            objects,
            workingScale);
        var execution = new SceneExecutionSnapshot(
            scene,
            camera,
            objects,
            lights,
            bounds,
            scene.Time,
            scene.DisableResourceShare,
            scene.BackgroundColor,
            scene.AmbientColor,
            scene.AmbientIntensity,
            gizmoTarget,
            scene.GizmoMode,
            textureBindings);
        RenderResource<SceneExecutionSnapshot> sceneToken = context.Borrow(execution);

        RenderResource[] resources =
        [
            sceneToken,
            .. textureBindings.Select(static item => item.Binding),
        ];
        OpaqueRenderDescription description = OpaqueRenderDescription.CreateBackendBoundary(
            RenderBackendBoundary.Graphics3D,
            state: sceneToken,
            execute: static (session, token) => session.UseResource(
                token,
                current => Render(session, current)),
            bounds: OpaqueRenderBoundsContract.Source(bounds),
            hitTest: RenderHitTestContract.OutputBounds,
            valueCardinality: RenderValueCardinality.ZeroOrOne,
            scale: RenderScaleContract.MaterializeAtWorkingScale,
            deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
            resources: resources);
        context.Publish(context.OpaqueSource(description));
    }

    private static SceneTextureBinding[] RecordDrawableTextures(
        RenderNodeContext context,
        IEnumerable<Object3D.Resource> objects,
        float outputScale)
    {
        var seen = new HashSet<IRecordedTextureSource>(ReferenceEqualityComparer.Instance);
        var result = new List<SceneTextureBinding>();
        foreach (Object3D.Resource obj in EnumerateObjects(objects))
        {
            Material3D.Resource? material = obj.Material;
            if (material is null)
                continue;

            foreach (IRecordedTextureSource source in material
                         .EnumerateTextureSources()
                         .OfType<IRecordedTextureSource>())
            {
                if (!seen.Add(source))
                    continue;
                float textureDensity = source.ResolveDensity(outputScale);
                RenderNode? root = source.RecordContent(textureDensity);
                if (root is null)
                    continue;

                RecordedNestedRenderTarget nested = context.RecordNestedTargetAtScale(
                    root,
                    source.TextureDomain,
                    textureDensity);
                result.Add(new SceneTextureBinding(source, nested.Binding));
            }
        }

        return [.. result];
    }

    private static IEnumerable<Object3D.Resource> EnumerateObjects(
        IEnumerable<Object3D.Resource> objects)
    {
        foreach (Object3D.Resource obj in objects)
        {
            if (!obj.IsEnabled)
                continue;

            yield return obj;
            foreach (Object3D.Resource child in EnumerateObjects(obj.GetChildResources()))
                yield return child;
        }
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

    private sealed record SceneExecutionSnapshot(
        Scene3D.Resource Scene,
        Camera3D.Resource Camera,
        Object3D.Resource[] Objects,
        Light3D.Resource[] Lights,
        Rect Bounds,
        TimeSpan Time,
        bool DisableResourceShare,
        Color BackgroundColor,
        Color AmbientColor,
        float AmbientIntensity,
        Object3D.Resource? GizmoTarget,
        GizmoMode GizmoMode,
        SceneTextureBinding[] TextureBindings);

    private sealed record SceneTextureBinding(
        IRecordedTextureSource Source,
        RenderResource<NestedRenderTargetBinding> Binding);

    private static void Render(OpaqueRenderSession session, SceneExecutionSnapshot snapshot)
    {
        UseTextureBindings(session, snapshot, index: 0, () => RenderCore(session, snapshot));
    }

    private static void UseTextureBindings(
        OpaqueRenderSession session,
        SceneExecutionSnapshot snapshot,
        int index,
        Action render)
    {
        if (index == snapshot.TextureBindings.Length)
        {
            render();
            return;
        }

        SceneTextureBinding current = snapshot.TextureBindings[index];
        session.UseResource(
            current.Binding,
            binding => NestedRenderTargetBindingScope.Use(
                current.Source,
                binding,
                () => UseTextureBindings(session, snapshot, index + 1, render)));
    }

    private static void RenderCore(OpaqueRenderSession session, SceneExecutionSnapshot snapshot)
    {
        IGraphicsContext? graphicsContext = GraphicsContextFactory.SharedContext;
        if (graphicsContext is null || !graphicsContext.Supports3DRendering)
            return;

        float density = session.WorkingScale;
        (int deviceWidth, int deviceHeight) = ResolveDeviceFootprint(snapshot.Bounds, density);
        Renderer3D renderer = snapshot.Scene.Renderer ??= new Renderer3D(graphicsContext);

        if (renderer.Width != deviceWidth || renderer.Height != deviceHeight)
        {
            try
            {
                if (renderer.Width == 0 || renderer.Height == 0)
                    renderer.Initialize(deviceWidth, deviceHeight);
                else
                    renderer.Resize(deviceWidth, deviceHeight);
            }
            catch (Exception ex)
            {
                if (session.Intent == RenderIntent.Delivery)
                {
                    s_logger.LogError(
                        ex,
                        "3D render surface allocation failed ({Width}x{Height} px, density {Scale}); delivery cannot omit the 3D value.",
                        deviceWidth,
                        deviceHeight,
                        density);
                }
                else
                {
                    s_logger.LogWarning(
                        ex,
                        "3D render surface allocation failed ({Width}x{Height} px, density {Scale}); dropping the 3D value for this preview frame.",
                        deviceWidth,
                        deviceHeight,
                        density);
                }

                snapshot.Scene.Renderer?.Dispose();
                snapshot.Scene.Renderer = null;
                ThrowIfDeliveryAllocationFailure(session.Intent, ex);
                return;
            }
        }

        renderer.SurfaceDensity = density;
        renderer.Render(
            new CompositionContext(snapshot.Time)
            {
                DisableResourceShare = snapshot.DisableResourceShare,
            },
            snapshot.Camera,
            snapshot.Objects,
            snapshot.Lights,
            snapshot.BackgroundColor,
            snapshot.AmbientColor,
            snapshot.AmbientIntensity,
            snapshot.GizmoTarget,
            snapshot.GizmoMode);

        using SKSurface? surface = renderer.CreateSkiaSurface();
        if (surface is null)
            return;

        using OpaqueRenderOutput output = session.CreateOutput(snapshot.Bounds);
        output.Canvas.Use(canvas =>
        {
            // This is the one deliberate backend hand-off: both surfaces have the same device
            // footprint, so copy in device space without exposing a raw target to public callbacks.
            using (canvas.PushDeviceSpace())
            {
                canvas.Canvas.DrawSurface(surface, 0, 0);
            }

            surface.Flush(true, true);
        });
        session.Publish(output);
    }

    /// <summary>Whether a device with <paramref name="budget"/> can render this scene at all.</summary>
    /// <remarks>
    /// Defers to <see cref="Renderer3D.CanInitialize"/> so the recording asks for every extent the
    /// allocation refuses on, including the fixed shadow extents that do not depend on the scene. An axis of
    /// zero is refused here rather than passed on, because the allocation refuses it as readily as one past
    /// a limit and <see cref="Renderer3D.CanInitialize"/> would report it as a caller error instead.
    /// </remarks>
    private static bool CanRenderScene(Device3DExtentBudget budget, int deviceWidth, int deviceHeight)
        => deviceWidth > 0
           && deviceHeight > 0
           && Renderer3D.CanInitialize(budget, deviceWidth, deviceHeight);

    /// <summary>The device extents a scene of <paramref name="bounds"/> asks for at <paramref name="density"/>.</summary>
    /// <remarks>
    /// One formula for both sides: <see cref="Process"/> decides whether the device can attach this, and
    /// <see cref="RenderCore"/> allocates it. Two spellings of the same rounding would let a recording pass a
    /// footprint the allocation then refuses, which is exactly the disagreement this answers for.
    /// </remarks>
    internal static (int Width, int Height) ResolveDeviceFootprint(Rect bounds, float density)
        => (ToDeviceExtent(bounds.Width, density), ToDeviceExtent(bounds.Height, density));

    // The working-scale clamp hands back an unclamped density when no candidate footprint fits, so an axis
    // can still arrive past int range. Saturating keeps that an extent the device refuses rather than a
    // negative one that would read as a caller error. An axis that rounds to zero is left at zero, which is
    // the extent the allocation already refuses.
    private static int ToDeviceExtent(double logicalExtent, float density)
    {
        double pixels = Math.Ceiling(logicalExtent * density);
        if (pixels >= int.MaxValue) return int.MaxValue;
        return pixels <= 0 ? 0 : (int)pixels;
    }

    internal static void ThrowIfDeliveryAllocationFailure(RenderIntent intent, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (intent == RenderIntent.Delivery)
            ExceptionDispatchInfo.Capture(exception).Throw();
    }

}

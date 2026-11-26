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
using Beutl.Graphics3D.Skyboxes;
using Beutl.Graphics3D.Textures;
using Beutl.Language;
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

    public (Scene3D.Resource Resource, int Version)? Scene { get; private set; } = scene.Capture();

    public bool Update(Scene3D.Resource scene)
    {
        bool changed = false;

        if (!scene.Compare(Scene))
        {
            Scene = scene.Capture();
            changed = true;
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
        if (!context.Supports3DRendering)
        {
            if (context.Purpose == RenderRequestPurpose.Frame
                || (context.Intent == RenderIntent.Delivery
                    && context.Purpose is not (RenderRequestPurpose.Bounds or RenderRequestPurpose.HitTest)))
            {
                RecordUnavailableScene(context, bounds, MessageStrings.Scene3DBackendUnavailable);
            }

            return;
        }

        float workingScale = RenderScaleContract.MaterializeAtWorkingScale.Resolve(
            [],
            bounds,
            context.OutputScale,
            context.MaxWorkingScale).Value;
        // Drop the 3D value before recording a hit test when the preview exceeds the device limit.
        // The 2D intermediate can be 1 px wider, as covered by ASceneExactlyAtTheDeviceLimit_IsAKnownGap.
        if (context.Intent == RenderIntent.Preview)
        {
            (int deviceWidth, int deviceHeight) = ResolveDeviceFootprint(bounds, workingScale);
            if (deviceWidth <= 0 || deviceHeight <= 0)
                return;

            if (Renderer3D.FindExceededExtentLimit(context.Device3DExtentBudget, deviceWidth, deviceHeight) is { } exceeded)
            {
                if (context.Purpose == RenderRequestPurpose.Frame)
                    RecordUnavailableScene(context, bounds, FormatExtentLimitMessage(exceeded));

                return;
            }
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
        // The gizmo is camera mode's handle on the selected object, drawn for the editor. An export renders the
        // same scene while camera mode may still have something selected, and must not carry the handle.
        Object3D.Resource? gizmoTarget = context.Intent == RenderIntent.Preview
            && !context.SuppressGizmos && scene.GizmoTarget is { } targetId
            ? FindObjectById(objects, targetId)
            : null;
        GizmoMode gizmoMode = gizmoTarget is null ? GizmoMode.None : scene.GizmoMode;
        Skybox3D.Resource? skybox = scene.Skybox is { IsEnabled: true, Opacity: > 0 } activeSkybox
            && activeSkybox.HasContent
            ? activeSkybox
            : null;
        SceneTextureBinding[] textureBindings = RecordDrawableTextures(
            context,
            objects,
            skybox,
            workingScale);
        var execution = new SceneExecutionSnapshot(
            scene,
            camera,
            objects,
            lights,
            bounds,
            scene.Time,
            scene.DisableResourceShare,
            scene.Conditions,
            scene.BackgroundColor,
            skybox,
            scene.AmbientColor,
            scene.AmbientIntensity,
            scene.AntiAliasing,
            gizmoTarget,
            gizmoMode,
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
            hitTest: RenderHitTestContract.Custom(
                new SceneHitTest(camera, objects, bounds, workingScale, scene.BackgroundColor.A > 0 || skybox is not null),
                static (state, context, point) => state.HitTest(context, point)),
            valueCardinality: RenderValueCardinality.ZeroOrOne,
            scale: RenderScaleContract.MaterializeAtWorkingScale,
            deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
            resources: resources);
        context.Publish(context.OpaqueSource(description));
    }

    private static SceneTextureBinding[] RecordDrawableTextures(
        RenderNodeContext context,
        IEnumerable<Object3D.Resource> objects,
        Skybox3D.Resource? skybox,
        float outputScale)
    {
        var seen = new HashSet<IRecordedTextureSource>(ReferenceEqualityComparer.Instance);
        var result = new List<SceneTextureBinding>();
        foreach (Object3D.Resource obj in EnumerateObjects(objects))
        {
            if (obj.Material is { } material)
                RecordSources(material.EnumerateTextureSources());
        }

        if (skybox is not null)
            RecordSources(skybox.EnumerateTextureSources());

        return [.. result];

        void RecordSources(IEnumerable<TextureSource.Resource> sources)
        {
            foreach (IRecordedTextureSource source in sources.OfType<IRecordedTextureSource>())
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
            if (obj.GetOriginal()?.Id.Value == targetId)
                return obj;

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
        MediaTime Time,
        bool DisableResourceShare,
        CompositionConditions Conditions,
        Color BackgroundColor,
        Skybox3D.Resource? Skybox,
        Color AmbientColor,
        float AmbientIntensity,
        AntiAliasingMode AntiAliasing,
        Object3D.Resource? GizmoTarget,
        GizmoMode GizmoMode,
        SceneTextureBinding[] TextureBindings);

    // A transparent background shows what lies beneath the scene, so there only the scene's objects answer;
    // an opaque one covers its whole rectangle.
    private sealed record SceneHitTest(
        Camera3D.Resource Camera,
        Object3D.Resource[] Objects,
        Rect Bounds,
        float WorkingScale,
        bool HasBackground)
    {
        public bool HitTest(RenderHitTestContext context, Point point)
        {
            if (!context.OutputBounds.Contains(point))
                return false;

            if (HasBackground)
                return true;

            // Test in the device pixels the scene is rendered at, so the camera's aspect ratio is the one
            // the rendering used.
            (int width, int height) = ResolveDeviceFootprint(Bounds, WorkingScale);
            return HitTester3D.HitTest(
                new Point((point.X - Bounds.X) * WorkingScale, (point.Y - Bounds.Y) * WorkingScale),
                width,
                height,
                Camera,
                Objects,
                WorkingScale) is not null;
        }
    }

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
        {
            ReportFailure(session, MessageStrings.Scene3DBackendUnavailable);
            return;
        }

        float density = session.OutputPlacement.Scale.Value;
        (int deviceWidth, int deviceHeight) = ResolveDeviceFootprint(snapshot.Bounds, density);
        int requestedSamples = snapshot.AntiAliasing switch
        {
            AntiAliasingMode.Msaa2x => 2,
            AntiAliasingMode.Msaa4x => 4,
            AntiAliasingMode.Msaa8x => 8,
            _ => 1,
        };
        Renderer3D renderer = snapshot.Scene.Renderer ??= new Renderer3D(graphicsContext);

        bool needsResize = renderer.Width != deviceWidth || renderer.Height != deviceHeight || renderer.RequestedSampleCount != requestedSamples;
        bool needsSkyboxTarget = snapshot.Skybox is not null && (needsResize || !renderer.HasSkyboxTarget);
        if (needsResize || needsSkyboxTarget)
        {
            if (needsResize && deviceWidth > 0 && deviceHeight > 0
                && Renderer3D.FindExceededExtentLimit(Device3DExtentBudget.FromContext(graphicsContext), deviceWidth, deviceHeight)
                    is { } exceeded)
            {
                ReportFailure(session, FormatExtentLimitMessage(exceeded));
                return;
            }

            try
            {
                if (needsResize)
                {
                    if (renderer.Width == 0 || renderer.Height == 0)
                        renderer.Initialize(deviceWidth, deviceHeight, requestedSamples);
                    else
                        renderer.Resize(deviceWidth, deviceHeight, requestedSamples);
                }

                if (needsSkyboxTarget)
                    renderer.EnsureSkyboxTarget();
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
                session.ReportPreviewWarning(MessageStrings.Scene3DSurfaceUnavailable);
                return;
            }

            if (needsResize && renderer.LastSampleAllocationFailure is { } failure)
            {
                s_logger.LogWarning(failure,
                    "3D multisample allocation failed ({Width}x{Height} px, requested {Requested}x). Rendering at {Samples}x.",
                    deviceWidth, deviceHeight, requestedSamples, renderer.SampleCount);
            }
        }

        if (session.Intent == RenderIntent.Preview && renderer.LastSampleAllocationFailure is not null)
            session.ReportPreviewWarning(string.Format(MessageStrings.Scene3DAntiAliasingReduced, renderer.SampleCount));

        renderer.SurfaceDensity = density;
        renderer.Render(
            new CompositionContext(snapshot.Time)
            {
                DisableResourceShare = snapshot.DisableResourceShare,
                Conditions = snapshot.Conditions,
            },
            snapshot.Camera,
            snapshot.Objects,
            snapshot.Lights,
            snapshot.BackgroundColor,
            snapshot.AmbientColor,
            snapshot.AmbientIntensity,
            snapshot.GizmoTarget,
            snapshot.GizmoMode,
            snapshot.Skybox);

        using SKSurface? surface = renderer.CreateSkiaSurface();
        if (surface is null)
        {
            ReportFailure(session, MessageStrings.Scene3DSurfaceUnavailable);
            return;
        }

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

    private static void RecordUnavailableScene(RenderNodeContext context, Rect bounds, string message)
    {
        // The request culls this source before executing the callback. Bypass the render cache so
        // captures and cache hits cannot change whether the warning runs for the requested region.
        context.DisableRenderCache();
        var unavailable = OpaqueRenderDescription.Create(
            state: message,
            execute: ReportFailure,
            bounds: OpaqueRenderBoundsContract.Source(bounds),
            hitTest: RenderHitTestContract.None,
            valueCardinality: RenderValueCardinality.ZeroOrOne,
            scale: RenderScaleContract.MaterializeAtWorkingScale,
            deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive);
        context.Publish(context.OpaqueSource(unavailable));
    }

    private static void ReportFailure(OpaqueRenderSession session, string message)
    {
        if (session.Intent == RenderIntent.Delivery)
            throw new InvalidOperationException(message);

        session.ReportPreviewWarning(message);
    }

    private static string FormatExtentLimitMessage((int Width, int Height, int Limit) exceeded)
        => string.Format(MessageStrings.Scene3DExtentLimitExceeded, exceeded.Width, exceeded.Height, exceeded.Limit);

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

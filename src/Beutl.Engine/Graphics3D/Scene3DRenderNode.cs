using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
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

        HasChanges = changed;
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
        float workingScale = RenderScaleContract.MaterializeAtWorkingScale.Resolve(
            [],
            bounds,
            context.OutputScale,
            context.MaxWorkingScale).Value;
        Object3D.Resource[] objects = scene.Objects.Where(static item => item.IsEnabled).ToArray();
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
        RenderResource<SceneExecutionSnapshot> sceneToken = context.Borrow(
            execution,
            new SceneSnapshotIdentity(scene.GetOriginal().Id, sceneSnapshot.Version),
            sceneSnapshot.Version);

        RenderResource[] resources =
        [
            sceneToken,
            .. textureBindings.Select(static item => item.Binding),
        ];
        OpaqueRenderDescription description = OpaqueRenderDescription.CreateBackendBoundary(
            RenderBackendBoundary.Graphics3D,
            execute: session => session.UseResource(
                sceneToken,
                current => Render(session, current)),
            bounds: RenderOperationBoundsContract.Source(bounds),
            hitTest: RenderHitTestContract.OutputBounds,
            valueCardinality: RenderValueCardinality.Single,
            scale: RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: typeof(Scene3DRenderNode),
            runtimeIdentity: new RenderRuntimeIdentity(
                new SceneRuntimeIdentity(scene.GetOriginal().Id, sceneSnapshot.Version, bounds)),
            resources: resources);
        context.Publish(context.OpaqueSource(description));
    }

    private static SceneTextureBinding[] RecordDrawableTextures(
        RenderNodeContext context,
        IEnumerable<Object3D.Resource> objects,
        float outputScale)
    {
        var seen = new HashSet<DrawableTextureSource.Resource>(ReferenceEqualityComparer.Instance);
        var result = new List<SceneTextureBinding>();
        foreach (Object3D.Resource obj in EnumerateObjects(objects))
        {
            Material3D.Resource? material = obj.Material;
            if (material is null)
                continue;

            foreach (DrawableTextureSource.Resource source in material
                         .EnumerateTextureSources()
                         .OfType<DrawableTextureSource.Resource>())
            {
                if (!seen.Add(source))
                    continue;
                DrawableRenderNode? root = source.RecordDrawable(outputScale);
                if (root is null)
                    continue;

                RecordedNestedRenderTarget nested = context.RecordNestedTargetAtScale(
                    root,
                    source.TextureDomain,
                    outputScale);
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
        DrawableTextureSource.Resource Source,
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
        int deviceWidth = (int)MathF.Ceiling((float)snapshot.Bounds.Width * density);
        int deviceHeight = (int)MathF.Ceiling((float)snapshot.Bounds.Height * density);
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
                s_logger.LogWarning(
                    ex,
                    "3D render surface allocation failed ({Width}x{Height} px, density {Scale}); dropping the 3D value for this frame.",
                    deviceWidth,
                    deviceHeight,
                    density);
                snapshot.Scene.Renderer?.Dispose();
                snapshot.Scene.Renderer = null;
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

    private readonly record struct SceneSnapshotIdentity(Guid SceneId, int Version);

    private readonly record struct SceneRuntimeIdentity(Guid SceneId, int Version, Rect Bounds);
}

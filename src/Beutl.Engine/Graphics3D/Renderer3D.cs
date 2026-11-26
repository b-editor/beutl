using System.Numerics;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Meshes;
using Beutl.Graphics3D.Nodes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics3D;

/// <summary>
/// Deferred 3D renderer using G-Buffer for lighting calculations.
/// Coordinates shadow, geometry, and lighting passes.
/// </summary>
internal sealed class Renderer3D : IRenderer3D
{
    private static readonly TextureFormat[] s_sampledAttachmentFormats =
        [.. GeometryPass.GBufferFormats, TextureFormat.RGBA16Float, TextureFormat.Depth32Float];

    private readonly IGraphicsContext _context;
    private readonly IShaderCompiler _shaderCompiler;
    private bool _disposed;

    private ViewportResources? _viewport;
    private ShadowManager? _shadowManager;

    private bool _hasRenderedDepth;
    private Camera3D.Resource? _lastCamera;
    private IReadOnlyList<Object3D.Resource>? _lastObjects;

    public Renderer3D(IGraphicsContext context, int sampleCount = 1)
    {
        AttachmentSampleCount.Validate(sampleCount);
        RequestedSampleCount = sampleCount;
        _context = context;
        _shaderCompiler = context.CreateShaderCompiler();
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int SampleCount { get; private set; } = 1;
    internal int RequestedSampleCount { get; private set; }
    internal Exception? LastSampleAllocationFailure { get; private set; }

    /// <summary>
    /// Device px per logical unit. Hit-test entry points multiply logical coordinates by this.
    /// </summary>
    public float SurfaceDensity { get; set; } = 1f;

    /// <summary>
    /// Finds the first required attachment that exceeds a reported device limit, in allocation order.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is zero or negative.</exception>
    internal static (int Width, int Height, int Limit)? FindExceededExtentLimit(
        Device3DExtentBudget budget, int width, int height)
    {
        if (!budget.CanAttach(width, height))
            return (width, height, budget.MaxAttachmentDimension);

        int shadowSize = ShadowPass.DefaultShadowMapSize;
        if (!budget.CanAttach(shadowSize, shadowSize))
            return (shadowSize, shadowSize, budget.MaxAttachmentDimension);

        int cubeFaceSize = PointShadowPass.DefaultCubeFaceSize;
        if (!budget.CanAttachCubeFaces(cubeFaceSize))
            return (cubeFaceSize, cubeFaceSize, budget.ResolveCubeFaceAttachmentBudget());

        return null;
    }

    /// <summary>
    /// Allocates the passes and the output texture for an extent.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The extent exceeds what the device can attach. Raised before any allocation, so nothing is left to
    /// clean up; <see cref="Scene3DRenderNode"/> drops the preview value or fails the delivery on it.
    /// </exception>
    public void Initialize(int width, int height)
        => Initialize(width, height, RequestedSampleCount);

    internal void Initialize(int width, int height, int sampleCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AttachmentSampleCount.Validate(sampleCount);

        DeviceExtentLimits.ThrowIfCannotAttach(_context, width, height);
        int shadowSize = ShadowPass.DefaultShadowMapSize;
        DeviceExtentLimits.ThrowIfCannotAttach(_context, shadowSize, shadowSize);
        DeviceExtentLimits.ThrowIfCannotAttachCubeFaces(_context, PointShadowPass.DefaultCubeFaceSize);
        ReplaceResources(width, height, sampleCount, initializeShadows: true);
    }

    /// <summary>
    /// Reallocates the passes and the output texture for a new extent.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The extent exceeds what the device can attach. The current extent and its resources are left as
    /// they were.
    /// </exception>
    public void Resize(int width, int height)
        => Resize(width, height, RequestedSampleCount);

    internal void Resize(int width, int height, int sampleCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AttachmentSampleCount.Validate(sampleCount);

        // Before the no-op check, so a zero or negative extent is refused rather than matched against the
        // "not yet initialized" state.
        DeviceExtentLimits.ThrowIfCannotAttach(_context, width, height);

        if (Width == width && Height == height && RequestedSampleCount == sampleCount)
            return;

        if (Width == 0)
            throw new InvalidOperationException("The renderer must be initialized before it is resized.");

        ReplaceResources(width, height, sampleCount, initializeShadows: false);
    }

    private void ReplaceResources(int width, int height, int requested, bool initializeShadows)
    {
        int sampleCount = requested > 1
            ? Math.Max(1, _context.GetSupportedSampleCount(requested, s_sampledAttachmentFormats))
            : 1;
        Exception? sampleAllocationFailure = null;
        ViewportResources? viewport = null;
        ShadowManager? shadows = null;
        try
        {
            if (initializeShadows)
            {
                shadows = new ShadowManager(_context, _shaderCompiler);
                shadows.Initialize();
            }

            while (true)
            {
                viewport = new ViewportResources(_context, _shaderCompiler);
                try
                {
                    viewport.Initialize(width, height, sampleCount);
                    break;
                }
                catch (Exception failure) when (sampleCount > 1)
                {
                    sampleAllocationFailure ??= failure;
                    ViewportResources failedViewport = viewport;
                    viewport = null;
                    failedViewport.Dispose();
                    // The backend may defer releases behind recorded work. Reclaim the failed
                    // viewport's memory before asking it for the next set of attachments.
                    _context.WaitIdle();
                    sampleCount /= 2;
                }
            }
        }
        catch
        {
            RenderNode3D.DisposeResources([viewport, shadows]);
            throw;
        }

        ViewportResources? oldViewport = _viewport;
        ShadowManager? oldShadows = initializeShadows ? _shadowManager : null;
        // Replacements need an owner even if retiring the old viewport throws.
        _viewport = viewport;
        _shadowManager = shadows ?? _shadowManager;
        Width = width;
        Height = height;
        RequestedSampleCount = requested;
        SampleCount = sampleCount;
        LastSampleAllocationFailure = sampleAllocationFailure;
        _hasRenderedDepth = false;

        RenderNode3D.DisposeResources([oldViewport, oldShadows]);
    }

    public void Render(
        CompositionContext compositionContext,
        Camera3D.Resource camera,
        IReadOnlyList<Object3D.Resource> objects,
        IReadOnlyList<Light3D.Resource> lights,
        Color backgroundColor,
        Color ambientColor,
        float ambientIntensity,
        Object3D.Resource? gizmoTarget = null,
        GizmoMode gizmoMode = GizmoMode.None)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _hasRenderedDepth = false;
        if (_viewport is not { } viewport || _shadowManager == null)
            return;

        _lastCamera = camera;
        _lastObjects = objects;

        float aspectRatio = (float)Width / Height;

        using var opaqueObjects = new PooledList<Object3D.Resource>();
        using var transparentObjects = new PooledList<TransparentObjectEntry>();
        SeparateObjectsByTransparency(objects, camera, opaqueObjects, transparentObjects);

        var (sceneCenter, sceneRadius) = CalculateSceneBounds(objects);

        _shadowManager.SurfaceDensity = SurfaceDensity;
        var lightToShadowIndex = _shadowManager.RenderShadows(lights, objects, sceneCenter, sceneRadius);
        var shadowUbo = _shadowManager.GetShadowUBO();

        using var lightDataList = new PooledList<LightData>();
        CollectLightData(lights, lightToShadowIndex, lightDataList);

        viewport.Geometry.Execute(compositionContext, camera, opaqueObjects, aspectRatio, lightDataList, ambientColor, ambientIntensity, SurfaceDensity);
        viewport.Geometry.PrepareForSampling();

        viewport.Lighting.BindGBuffer(viewport.Geometry);
        viewport.Lighting.BindShadowMaps(_shadowManager);
        viewport.Lighting.Execute(camera, lightDataList, backgroundColor, ambientColor, ambientIntensity, shadowUbo);

        if (transparentObjects.Count > 0)
        {
            // Transparent fragments blend per sample over averaged lighting. At an opaque silhouette,
            // a transparent object behind it can tint the opaque share of the pixel.
            viewport.Transparent.Execute(compositionContext, camera, transparentObjects, lightDataList,
                ambientColor, ambientIntensity, aspectRatio, SurfaceDensity);
        }

        viewport.DepthResolve?.Execute();

        if (gizmoTarget != null && gizmoMode != GizmoMode.None)
        {
            viewport.Gizmo.Execute(
                camera,
                gizmoTarget,
                GetWorldPosition(objects, gizmoTarget),
                GetWorldOrientation(objects, gizmoTarget),
                gizmoMode,
                aspectRatio);
        }

        viewport.Lighting.OutputTexture!.PrepareForSampling();
        viewport.Flip.Execute();
        viewport.Flip.PrepareForSampling();

        _context.CopyTexture(viewport.Flip.OutputTexture!, viewport.Output);
        _hasRenderedDepth = true;
    }

    /// <summary>
    /// Converts the enabled lights to shader-compatible <see cref="LightData"/>, at most
    /// <see cref="RenderContext3D.MaxLights"/>, giving each light that casts a shadow its shadow index.
    /// </summary>
    /// <remarks>The light index counts disabled lights too, because the shadow manager keys its map by list index.</remarks>
    private static void CollectLightData(
        IReadOnlyList<Light3D.Resource> lights,
        Dictionary<int, int> lightToShadowIndex,
        PooledList<LightData> lightDataList)
    {
        int lightIndex = 0;
        foreach (var light in lights)
        {
            if (!light.IsEnabled)
            {
                lightIndex++;
                continue;
            }

            var lightData = LightData.FromLight(light);

            if (lightToShadowIndex.TryGetValue(lightIndex, out int shadowIdx))
            {
                lightData.ShadowIndex = shadowIdx;
            }

            lightDataList.Add(lightData);
            lightIndex++;

            if (lightDataList.Count >= RenderContext3D.MaxLights)
                break;
        }
    }

    /// <summary>
    /// Separates objects into opaque and transparent lists.
    /// Transparent objects are sorted by depth along the camera's view (far to near); objects at the same depth,
    /// such as unmoved 2D cards, keep the scene's order, as 2D drawing would.
    /// </summary>
    private static void SeparateObjectsByTransparency(
        IReadOnlyList<Object3D.Resource> objects, Camera3D.Resource camera,
        PooledList<Object3D.Resource> opaqueObjects, PooledList<TransparentObjectEntry> transparentEntries)
    {
        foreach (var obj in objects)
        {
            if (obj.IsEnabled)
                opaqueObjects.Add(obj);
        }

        Object3DTraversal.ForEachEnabled(objects, (obj, world) =>
        {
            if (IsTransparent(obj))
            {
                transparentEntries.Add(new TransparentObjectEntry
                {
                    Object = obj,
                    WorldMatrix = world,
                    DistanceToCamera = GizmoHitTester.GetViewDepth(
                        camera.Position,
                        camera.Target,
                        float.NegativeInfinity,
                        world.Translation),
                    Order = transparentEntries.Count,
                });
            }
        });

        transparentEntries.Sort(static (a, b) =>
        {
            int byDepth = b.DistanceToCamera.CompareTo(a.DistanceToCamera);
            return byDepth != 0 ? byDepth : a.Order.CompareTo(b.Order);
        });
    }

    /// <summary>
    /// Determines if an object uses a transparent material.
    /// </summary>
    private static bool IsTransparent(Object3D.Resource obj)
    {
        var material = obj.Material;
        return material?.IsTransparent ?? false;
    }

    /// <summary>
    /// Calculates the bounding sphere of all visible objects in the scene.
    /// Used for directional light shadow mapping.
    /// </summary>
    private static (Vector3 Center, float Radius) CalculateSceneBounds(IReadOnlyList<Object3D.Resource> objects)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        Object3DTraversal.ForEachEnabled(objects, (obj, world) =>
        {
            // Only shadow casters and lit receivers need to be in the shadow map; a transparent surface such
            // as a 2D card does not sample it.
            bool takesPartInShadows = obj.CastsVisibleShadow
                || (obj.ReceiveShadows && obj.Material?.IsTransparent != true);
            if (takesPartInShadows && obj.GetMesh() is { VertexCount: > 0 } mesh)
            {
                BoundingBox box = mesh.GetBoundingBox();
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? box.Min.X : box.Max.X,
                        (i & 2) == 0 ? box.Min.Y : box.Max.Y,
                        (i & 4) == 0 ? box.Min.Z : box.Max.Z);
                    Vector3 transformed = Vector3.Transform(corner, world);
                    min = Vector3.Min(min, transformed);
                    max = Vector3.Max(max, transformed);
                }
            }
        });

        if (min.X == float.MaxValue)
            return (Vector3.Zero, 1f);

        var center = (min + max) * 0.5f;
        var radius = Math.Max(Vector3.Distance(min, max) * 0.5f, 1f);
        return (center, radius);
    }

    /// <summary>
    /// Where <paramref name="target"/> sits in the scene, with the transforms of the groups it is nested in.
    /// </summary>
    internal static Vector3 GetWorldPosition(IReadOnlyList<Object3D.Resource> roots, Object3D.Resource target)
    {
        return (target.GetWorldMatrix() * GetParentWorldMatrix(roots, target)).Translation;
    }

    /// <summary>
    /// How <paramref name="target"/> is turned in the scene: its own rotation followed by the rotation of the
    /// groups it is nested in, without their scale.
    /// </summary>
    internal static Quaternion GetWorldOrientation(IReadOnlyList<Object3D.Resource> roots, Object3D.Resource target)
    {
        Quaternion local = Quaternion.CreateFromYawPitchRoll(
            target.Rotation.Y * MathF.PI / 180f,
            target.Rotation.X * MathF.PI / 180f,
            target.Rotation.Z * MathF.PI / 180f);
        return Quaternion.Concatenate(local, GetRotation(GetParentWorldMatrix(roots, target)));
    }

    // The rotation part of a transform that may also scale unevenly or shear: its axes made orthonormal.
    private static Quaternion GetRotation(Matrix4x4 matrix)
    {
        var x = new Vector3(matrix.M11, matrix.M12, matrix.M13);
        var y = new Vector3(matrix.M21, matrix.M22, matrix.M23);
        if (x.LengthSquared() < 1e-12f || y.LengthSquared() < 1e-12f)
            return Quaternion.Identity;

        x = Vector3.Normalize(x);
        y -= x * Vector3.Dot(x, y);
        if (y.LengthSquared() < 1e-12f)
            return Quaternion.Identity;

        y = Vector3.Normalize(y);
        Vector3 z = Vector3.Cross(x, y);
        var rotation = new Matrix4x4(
            x.X, x.Y, x.Z, 0,
            y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0,
            0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotation));
    }

    /// <summary>
    /// The combined transform of the groups <paramref name="target"/> is nested in, or the identity for an
    /// object at the root.
    /// </summary>
    internal static Matrix4x4 GetParentWorldMatrix(IReadOnlyList<Object3D.Resource> roots, Object3D.Resource target)
    {
        return TryFindParentMatrix(roots, target, Matrix4x4.Identity, out Matrix4x4 parent)
            ? parent
            : Matrix4x4.Identity;

        static bool TryFindParentMatrix(
            IReadOnlyList<Object3D.Resource> objects,
            Object3D.Resource target,
            Matrix4x4 parentMatrix,
            out Matrix4x4 parent)
        {
            foreach (Object3D.Resource obj in objects)
            {
                if (ReferenceEquals(obj, target))
                {
                    parent = parentMatrix;
                    return true;
                }

                if (TryFindParentMatrix(obj.GetChildResources(), target, obj.GetWorldMatrix() * parentMatrix, out parent))
                    return true;
            }

            parent = default;
            return false;
        }
    }

    public SKSurface? CreateSkiaSurface()
    {
        ITexture2D? output = _viewport?.Output;
        output?.PrepareForSkiaSampling(requireCompletion: false);
        return output?.CreateSkiaSurface();
    }

    internal bool TryCopyDepth(Span<float> destination, out PixelSize size)
    {
        size = default;
        if (_disposed || !_hasRenderedDepth || _viewport is not { } viewport) return false;
        ITexture2D depth = (viewport.DepthResolve?.OutputTexture ?? viewport.Geometry.DepthTexture)!;
        size = new PixelSize(Width, Height);
        if (destination.Length < (long)size.Width * size.Height) return false;

        var data = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(depth.DownloadPixels());
        // Mirror the final color pass, which flips the offscreen framebuffer vertically.
        for (int y = 0; y < size.Height; y++)
            data.Slice((size.Height - 1 - y) * size.Width, size.Width).CopyTo(destination.Slice(y * size.Width));
        return true;
    }

    public byte[] DownloadPixels()
    {
        return _viewport?.Output.DownloadPixels() ?? [];
    }

    public Object3D.Resource? HitTest(Point screenPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_lastCamera == null || _lastObjects == null || _lastObjects.Count == 0)
            return null;

        return HitTester3D.HitTest(ToDevice(screenPoint), Width, Height, _lastCamera, _lastObjects, SurfaceDensity);
    }

    /// <summary>
    /// Performs hit testing and returns the path from root to the hit object.
    /// </summary>
    /// <param name="screenPoint">The point in LOGICAL render coordinates (<c>Scene3D.RenderWidth/Height</c> space).</param>
    /// <returns>A list representing the path from root to the hit object, or empty if none.</returns>
    public IReadOnlyList<Object3D.Resource> HitTestWithPath(Point screenPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_lastCamera == null || _lastObjects == null || _lastObjects.Count == 0)
            return [];

        return HitTester3D.HitTestWithPath(ToDevice(screenPoint), Width, Height, _lastCamera, _lastObjects, SurfaceDensity);
    }

    public GizmoAxis GizmoHitTest(Point screenPoint, Object3D.Resource? gizmoTarget, GizmoMode gizmoMode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_lastCamera == null || gizmoTarget == null || gizmoMode == GizmoMode.None)
            return GizmoAxis.None;

        return GizmoHitTester.HitTest(
            ToDevice(screenPoint),
            Width,
            Height,
            _lastCamera,
            GetWorldPosition(_lastObjects ?? [], gizmoTarget),
            GetWorldOrientation(_lastObjects ?? [], gizmoTarget),
            gizmoMode);
    }

    // Logical input -> device px to match Width/Height (see SurfaceDensity).
    private Point ToDevice(Point logicalPoint) =>
        SurfaceDensity == 1f ? logicalPoint : logicalPoint * SurfaceDensity;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        RenderNode3D.DisposeResources([_viewport, _shadowManager, _shaderCompiler as IDisposable]);
    }

    private sealed class ViewportResources(IGraphicsContext context, IShaderCompiler compiler) : IDisposable
    {
        public GeometryPass Geometry { get; private set; } = null!;
        public LightingPass Lighting { get; private set; } = null!;
        public TransparentPass Transparent { get; private set; } = null!;
        public GizmoPass Gizmo { get; private set; } = null!;
        public FlipPass Flip { get; private set; } = null!;
        public DepthResolvePass? DepthResolve { get; private set; }
        public ITexture2D Output { get; private set; } = null!;

        public void Initialize(int width, int height, int sampleCount)
        {
            Geometry = new GeometryPass(context, compiler, sampleCount);
            Geometry.Initialize(width, height);
            ITexture2D depth = Geometry.DepthTexture!;

            Lighting = new LightingPass(context, compiler, depth);
            Lighting.Initialize(width, height);
            ITexture2D litColor = Lighting.OutputTexture!;

            Transparent = new TransparentPass(context, compiler, depth);
            Transparent.SetColorTexture(litColor);
            Transparent.Initialize(width, height);

            Gizmo = new GizmoPass(context, compiler, depth);
            Gizmo.Initialize(width, height);
            Gizmo.SetColorTexture(litColor);

            if (sampleCount > 1)
            {
                DepthResolve = new DepthResolvePass(context, compiler, depth);
                DepthResolve.Initialize(width, height);
            }

            Flip = new FlipPass(context, compiler);
            Flip.SetInputTexture(litColor);
            Flip.Initialize(width, height);

            Output = context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        }

        public void Dispose()
        {
            RenderNode3D.DisposeResources([Flip, DepthResolve, Gizmo, Transparent, Lighting, Geometry, Output]);
        }
    }
}

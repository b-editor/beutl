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
    private readonly IGraphicsContext _context;
    private readonly IShaderCompiler _shaderCompiler;
    private bool _disposed;

    // Render passes
    private GeometryPass? _geometryPass;
    private LightingPass? _lightingPass;
    private TransparentPass? _transparentPass;
    private GizmoPass? _gizmoPass;
    private FlipPass? _flipPass;

    // Shadow management
    private ShadowManager? _shadowManager;

    // Final output for Skia integration
    private ITexture2D? _outputTexture;

    // Cached render state for hit testing
    private Camera3D.Resource? _lastCamera;
    private IReadOnlyList<Object3D.Resource>? _lastObjects;

    public Renderer3D(IGraphicsContext context)
    {
        _context = context;
        _shaderCompiler = context.CreateShaderCompiler();
    }

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>
    /// Device px per logical unit. Hit-test entry points multiply logical coordinates by this.
    /// </summary>
    public float SurfaceDensity { get; set; } = 1f;

    /// <summary>
    /// Whether a device with <paramref name="budget"/> could <see cref="Initialize"/> this extent.
    /// </summary>
    /// <remarks>
    /// The extent-limit half of <see cref="Initialize"/>, asked without a device and without allocating, so
    /// a recording can answer for what the execution will do. It covers the three extents
    /// <see cref="Initialize"/> refuses on: the output texture the caller sized, and the two fixed shadow
    /// extents <see cref="ShadowManager"/> allocates whatever the scene is - a device that cannot attach
    /// those refuses every 3D scene, not only a large one. It deliberately does not predict a driver's
    /// out-of-memory or any other runtime failure, which stay the allocation's to report.
    /// <para>
    /// This lives beside <see cref="Initialize"/> so the two are read together: an allocation added there
    /// whose extent a device can refuse belongs here as well.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is zero or negative.</exception>
    internal static bool CanInitialize(Device3DExtentBudget budget, int width, int height)
        => budget.CanAttach(width, height)
           && budget.CanAttach(ShadowPass.DefaultShadowMapSize, ShadowPass.DefaultShadowMapSize)
           && budget.CanAttachCubeFaces(PointShadowPass.DefaultCubeFaceSize);

    /// <summary>
    /// Allocates the passes and the output texture for an extent.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The extent exceeds what the device can attach. Raised before any allocation, so nothing is left to
    /// clean up; <see cref="Scene3DRenderNode"/> drops the preview value or fails the delivery on it.
    /// </exception>
    public void Initialize(int width, int height)
    {
        // The output texture below is allocated outside any RenderNode3D, so the device limit has to be
        // asked here rather than left to the passes.
        DeviceExtentLimits.ThrowIfCannotAttach(_context, width, height);

        // Commit Width/Height only after all allocations succeed, so a failure is retryable.
        ShadowManager? shadowManager = null;
        GeometryPass? geometryPass = null;
        LightingPass? lightingPass = null;
        TransparentPass? transparentPass = null;
        GizmoPass? gizmoPass = null;
        FlipPass? flipPass = null;
        ITexture2D? outputTexture = null;
        try
        {
            // The shadow maps are a fixed size the device may not be able to attach. Allocating them here
            // rather than lazily on the first Render keeps that refusal on this path, which the caller
            // already handles, instead of raising it mid-frame with a half-built manager left behind.
            shadowManager = new ShadowManager(_context, _shaderCompiler);
            shadowManager.Initialize();

            geometryPass = new GeometryPass(_context, _shaderCompiler);
            geometryPass.Initialize(width, height);

            lightingPass = new LightingPass(_context, _shaderCompiler, geometryPass.DepthTexture!);
            lightingPass.Initialize(width, height);

            transparentPass = new TransparentPass(_context, _shaderCompiler, geometryPass.DepthTexture!);
            transparentPass.Initialize(width, height);

            gizmoPass = new GizmoPass(_context, _shaderCompiler, geometryPass.DepthTexture!);
            gizmoPass.Initialize(width, height);

            flipPass = new FlipPass(_context, _shaderCompiler);
            flipPass.Initialize(width, height);

            outputTexture = _context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        }
        catch
        {
            outputTexture?.Dispose();
            flipPass?.Dispose();
            gizmoPass?.Dispose();
            transparentPass?.Dispose();
            lightingPass?.Dispose();
            geometryPass?.Dispose();
            shadowManager?.Dispose();
            throw;
        }

        _shadowManager = shadowManager;
        _geometryPass = geometryPass;
        _lightingPass = lightingPass;
        _transparentPass = transparentPass;
        _gizmoPass = gizmoPass;
        _flipPass = flipPass;
        _outputTexture = outputTexture;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Reallocates the passes and the output texture for a new extent.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The extent exceeds what the device can attach. The current extent and its resources are left as
    /// they were.
    /// </exception>
    public void Resize(int width, int height)
    {
        // Before the no-op check, so a zero or negative extent is refused rather than matched against the
        // "not yet initialized" state.
        DeviceExtentLimits.ThrowIfCannotAttach(_context, width, height);

        if (Width == width && Height == height)
            return;

        // Allocate into locals first; old fields stay intact on failure.
        GeometryPass? geometryPass = null;
        LightingPass? lightingPass = null;
        TransparentPass? transparentPass = null;
        GizmoPass? gizmoPass = null;
        FlipPass? flipPass = null;
        ITexture2D? outputTexture = null;
        try
        {
            geometryPass = _geometryPass;
            geometryPass?.Resize(width, height);

            if (geometryPass?.DepthTexture != null)
            {
                lightingPass = new LightingPass(_context, _shaderCompiler, geometryPass.DepthTexture);
                lightingPass.Initialize(width, height);

                transparentPass = new TransparentPass(_context, _shaderCompiler, geometryPass.DepthTexture);
                transparentPass.Initialize(width, height);

                gizmoPass = new GizmoPass(_context, _shaderCompiler, geometryPass.DepthTexture);
                gizmoPass.Initialize(width, height);
            }

            flipPass = _flipPass;
            flipPass?.Resize(width, height);

            outputTexture = _context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        }
        catch
        {
            outputTexture?.Dispose();
            if (gizmoPass != _gizmoPass) gizmoPass?.Dispose();
            if (transparentPass != _transparentPass) transparentPass?.Dispose();
            if (lightingPass != _lightingPass) lightingPass?.Dispose();
            throw;
        }

        if (_lightingPass != lightingPass) _lightingPass?.Dispose();
        if (_transparentPass != transparentPass) _transparentPass?.Dispose();
        if (_gizmoPass != gizmoPass) _gizmoPass?.Dispose();
        _outputTexture?.Dispose();

        _lightingPass = lightingPass;
        _transparentPass = transparentPass;
        _gizmoPass = gizmoPass;
        _outputTexture = outputTexture;
        Width = width;
        Height = height;
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

        if (_geometryPass == null || _lightingPass == null || _transparentPass == null ||
            _gizmoPass == null || _flipPass == null || _shadowManager == null)
            return;

        // Cache for hit testing
        _lastCamera = camera;
        _lastObjects = objects;

        float aspectRatio = (float)Width / Height;

        // Separate objects into opaque and transparent

        using var opaqueObjects = new PooledList<Object3D.Resource>();
        using var transparentObjects = new PooledList<TransparentObjectEntry>();
        SeparateObjectsByTransparency(objects, camera, opaqueObjects, transparentObjects);

        // Calculate scene bounds for shadow mapping
        var (sceneCenter, sceneRadius) = CalculateSceneBounds(objects);

        // === SHADOW PASS ===
        var lightToShadowIndex = _shadowManager.RenderShadows(lights, objects, sceneCenter, sceneRadius);
        var shadowUbo = _shadowManager.GetShadowUBO();
        _shadowManager.PrepareForSampling();

        // Convert light resources to shader-compatible LightData
        using var lightDataList = new PooledList<LightData>();
        int lightIndex = 0;
        foreach (var light in lights)
        {
            if (!light.IsEnabled)
            {
                lightIndex++;
                continue;
            }

            var lightData = LightData.FromLight(light);

            // Set shadow index if this light casts shadows
            if (lightToShadowIndex.TryGetValue(lightIndex, out int shadowIdx))
            {
                lightData.ShadowIndex = shadowIdx;
            }

            lightDataList.Add(lightData);
            lightIndex++;

            if (lightDataList.Count >= RenderContext3D.MaxLights)
                break;
        }

        // === GEOMETRY PASS (opaque objects only) ===
        _geometryPass.Execute(compositionContext, camera, opaqueObjects, aspectRatio, lightDataList, ambientColor, ambientIntensity, SurfaceDensity);
        _geometryPass.PrepareForSampling();

        // === LIGHTING PASS ===
        _lightingPass.BindGBuffer(_geometryPass);
        _lightingPass.BindShadowMaps(_shadowManager);
        _lightingPass.Execute(camera, lightDataList, backgroundColor, ambientColor, ambientIntensity, shadowUbo);
        _lightingPass.PrepareForSampling();

        // Determine the texture to pass to subsequent passes
        ITexture2D? colorOutput = _lightingPass.OutputTexture;

        // === TRANSPARENT PASS ===
        if (transparentObjects.Count > 0)
        {
            _transparentPass.SetColorTexture(_lightingPass.OutputTexture!);
            _transparentPass.Execute(compositionContext, camera, transparentObjects, lightDataList, ambientColor, ambientIntensity, aspectRatio, SurfaceDensity);
            _transparentPass.PrepareForSampling();
            colorOutput = _transparentPass.OutputTexture;
        }

        // === GIZMO PASS ===
        if (gizmoTarget != null && gizmoMode != GizmoMode.None)
        {
            _gizmoPass.SetColorTexture(colorOutput!);
            _gizmoPass.Execute(camera, gizmoTarget, GetWorldPosition(objects, gizmoTarget), gizmoMode, aspectRatio);
            _gizmoPass.PrepareForSampling();
            colorOutput = _gizmoPass.OutputTexture;
        }

        // === FLIP PASS ===
        _flipPass.SetInputTexture(colorOutput!);
        _flipPass.Execute();
        _flipPass.PrepareForSampling();

        // Copy result to output texture for Skia integration
        CopyToOutputTexture();
    }

    /// <summary>
    /// Separates objects into opaque and transparent lists.
    /// Transparent objects are sorted by distance from camera (far to near).
    /// </summary>
    private static void SeparateObjectsByTransparency(
        IReadOnlyList<Object3D.Resource> objects, Camera3D.Resource camera,
        PooledList<Object3D.Resource> opaqueObjects, PooledList<TransparentObjectEntry> transparentEntries)
    {
        foreach (var obj in objects)
        {
            if (!obj.IsEnabled) continue;
            opaqueObjects.Add(obj);
            CollectTransparent(obj, Matrix4x4.Identity);
        }

        void CollectTransparent(Object3D.Resource obj, Matrix4x4 parentMatrix)
        {
            if (!obj.IsEnabled) return;
            Matrix4x4 world = obj.GetWorldMatrix() * parentMatrix;
            foreach (Object3D.Resource child in obj.GetChildResources())
                CollectTransparent(child, world);
            if (IsTransparent(obj))
            {
                transparentEntries.Add(new TransparentObjectEntry
                {
                    Object = obj,
                    WorldMatrix = world,
                    DistanceToCamera = Vector3.Distance(world.Translation, camera.Position),
                });
            }
        }

        // Sort transparent objects from far to near (painter's algorithm)
        transparentEntries.Sort((a, b) => b.DistanceToCamera.CompareTo(a.DistanceToCamera));
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

        foreach (Object3D.Resource obj in objects)
        {
            Include(obj, Matrix4x4.Identity);
        }

        if (min.X == float.MaxValue)
            return (Vector3.Zero, 1f);

        var center = (min + max) * 0.5f;
        var radius = Math.Max(Vector3.Distance(min, max) * 0.5f, 1f);
        return (center, radius);

        void Include(Object3D.Resource obj, Matrix4x4 parentMatrix)
        {
            if (!obj.IsEnabled)
                return;

            Matrix4x4 world = obj.GetWorldMatrix() * parentMatrix;
            if (obj.GetMesh() is { VertexCount: > 0 } mesh)
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

            foreach (Object3D.Resource child in obj.GetChildResources())
            {
                Include(child, world);
            }
        }
    }

    /// <summary>
    /// Where <paramref name="target"/> sits in the scene, with the transforms of the groups it is nested in.
    /// </summary>
    internal static Vector3 GetWorldPosition(IReadOnlyList<Object3D.Resource> roots, Object3D.Resource target)
    {
        return TryGetWorldMatrix(roots, target, Matrix4x4.Identity, out Matrix4x4 world)
            ? world.Translation
            : target.GetWorldMatrix().Translation;

        static bool TryGetWorldMatrix(
            IReadOnlyList<Object3D.Resource> objects,
            Object3D.Resource target,
            Matrix4x4 parentMatrix,
            out Matrix4x4 world)
        {
            foreach (Object3D.Resource obj in objects)
            {
                Matrix4x4 matrix = obj.GetWorldMatrix() * parentMatrix;
                if (ReferenceEquals(obj, target))
                {
                    world = matrix;
                    return true;
                }

                if (TryGetWorldMatrix(obj.GetChildResources(), target, matrix, out world))
                    return true;
            }

            world = default;
            return false;
        }
    }

    private void CopyToOutputTexture()
    {
        if (_flipPass?.OutputTexture == null || _outputTexture == null)
            return;

        // Copy from flip pass output to shared output texture
        _context.CopyTexture(_flipPass.OutputTexture, _outputTexture);
    }

    public SKSurface? CreateSkiaSurface()
    {
        _outputTexture?.PrepareForSkiaSampling(requireCompletion: false);
        return _outputTexture?.CreateSkiaSurface();
    }

    public byte[] DownloadPixels()
    {
        return _outputTexture?.DownloadPixels() ?? [];
    }

    public Object3D.Resource? HitTest(Point screenPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_lastCamera == null || _lastObjects == null || _lastObjects.Count == 0)
            return null;

        return HitTester3D.HitTest(ToDevice(screenPoint), Width, Height, _lastCamera, _lastObjects);
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

        return HitTester3D.HitTestWithPath(ToDevice(screenPoint), Width, Height, _lastCamera, _lastObjects);
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
            gizmoTarget.Rotation,
            gizmoMode);
    }

    // Logical input -> device px to match Width/Height (see SurfaceDensity).
    private Point ToDevice(Point logicalPoint) =>
        SurfaceDensity == 1f ? logicalPoint : logicalPoint * SurfaceDensity;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _flipPass?.Dispose();
        _gizmoPass?.Dispose();
        _transparentPass?.Dispose();
        _lightingPass?.Dispose();
        _geometryPass?.Dispose();
        _shadowManager?.Dispose();
        _outputTexture?.Dispose();

        (_shaderCompiler as IDisposable)?.Dispose();
    }
}

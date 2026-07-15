using System.Numerics;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Nodes;
using Beutl.Media;
using SkiaSharp;
using RenderIntent = Beutl.Graphics.Rendering.RenderIntent;
using RenderPullPurpose = Beutl.Graphics.Rendering.RenderPullPurpose;

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

    public void Initialize(int width, int height)
    {
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
            shadowManager = new ShadowManager(_context, _shaderCompiler);

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

    public void Resize(int width, int height)
    {
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
        RenderIntent renderIntent,
        RenderPullPurpose pullPurpose,
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
        _geometryPass.Execute(
            compositionContext, camera, opaqueObjects, aspectRatio, lightDataList, ambientColor, ambientIntensity,
            renderIntent, pullPurpose, SurfaceDensity);
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
            _transparentPass.Execute(
                compositionContext, camera, transparentObjects, lightDataList, ambientColor, ambientIntensity,
                aspectRatio, renderIntent, pullPurpose, SurfaceDensity);
            _transparentPass.PrepareForSampling();
            colorOutput = _transparentPass.OutputTexture;
        }

        // === GIZMO PASS ===
        if (gizmoTarget != null && gizmoMode != GizmoMode.None)
        {
            _gizmoPass.SetColorTexture(colorOutput!);
            _gizmoPass.Execute(camera, gizmoTarget, gizmoMode, aspectRatio);
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
            if (!obj.IsEnabled)
                continue;

            if (IsTransparent(obj))
            {
                // Calculate distance to camera for sorting
                float distance = Vector3.Distance(obj.Position, camera.Position);
                transparentEntries.Add(new TransparentObjectEntry
                {
                    Object = obj,
                    WorldMatrix = obj.GetWorldMatrix(),
                    DistanceToCamera = distance
                });
            }
            else
            {
                opaqueObjects.Add(obj);
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
        if (objects.Count == 0)
            return (Vector3.Zero, 10f);

        // Calculate bounding box from all object positions
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var obj in objects)
        {
            if (!obj.IsEnabled)
                continue;

            var pos = obj.Position;
            var scale = obj.Scale;

            // Approximate object bounds (position ± scale)
            min = Vector3.Min(min, pos - scale);
            max = Vector3.Max(max, pos + scale);
        }

        // If no visible objects, return default bounds
        if (min.X == float.MaxValue)
            return (Vector3.Zero, 10f);

        // Calculate center and radius
        var center = (min + max) * 0.5f;
        var radius = Vector3.Distance(min, max) * 0.5f;

        // Ensure minimum radius
        radius = Math.Max(radius, 5f);

        return (center, radius);
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
            gizmoTarget.Position,
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

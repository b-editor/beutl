using System.Collections.Generic;
using System.Numerics;
using Beutl.Collections.Pooled;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Lighting;
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

    public void Initialize(int width, int height)
    {
        Width = width;
        Height = height;

        // Create shadow manager
        _shadowManager = new ShadowManager(_context, _shaderCompiler);

        // Create geometry pass
        _geometryPass = new GeometryPass(_context, _shaderCompiler);
        _geometryPass.Initialize(width, height);

        // Create lighting pass (uses depth texture from geometry pass)
        _lightingPass = new LightingPass(_context, _shaderCompiler, _geometryPass.DepthTexture!);
        _lightingPass.Initialize(width, height);

        // Create transparent pass (uses depth texture from geometry pass for depth testing)
        _transparentPass = new TransparentPass(_context, _shaderCompiler, _geometryPass.DepthTexture!);
        _transparentPass.Initialize(width, height);

        // Create gizmo pass (uses depth texture from geometry pass)
        _gizmoPass = new GizmoPass(_context, _shaderCompiler, _geometryPass.DepthTexture!);
        _gizmoPass.Initialize(width, height);

        // Create flip pass (corrects vertical orientation)
        _flipPass = new FlipPass(_context, _shaderCompiler);
        _flipPass.Initialize(width, height);

        // Create output texture for Skia integration
        _outputTexture = _context.CreateTexture2D(width, height, TextureFormat.BGRA8Unorm);
    }

    public void Resize(int width, int height)
    {
        if (Width == width && Height == height)
            return;

        Width = width;
        Height = height;

        // Resize geometry pass
        _geometryPass?.Resize(width, height);

        // Recreate lighting pass to use new depth texture
        _lightingPass?.Dispose();
        if (_geometryPass?.DepthTexture != null)
        {
            _lightingPass = new LightingPass(_context, _shaderCompiler, _geometryPass.DepthTexture);
            _lightingPass.Initialize(width, height);
        }

        // Recreate transparent pass to use new depth texture
        _transparentPass?.Dispose();
        if (_geometryPass?.DepthTexture != null)
        {
            _transparentPass = new TransparentPass(_context, _shaderCompiler, _geometryPass.DepthTexture);
            _transparentPass.Initialize(width, height);
        }

        // Recreate gizmo pass to use new depth texture
        _gizmoPass?.Dispose();
        if (_geometryPass?.DepthTexture != null)
        {
            _gizmoPass = new GizmoPass(_context, _shaderCompiler, _geometryPass.DepthTexture);
            _gizmoPass.Initialize(width, height);
        }

        // Resize flip pass
        _flipPass?.Resize(width, height);

        // Recreate output texture
        _outputTexture?.Dispose();
        _outputTexture = _context.CreateTexture2D(width, height, TextureFormat.BGRA8Unorm);
    }

    public void Render(
        RenderContext renderContext,
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
        _geometryPass.Execute(renderContext, camera, opaqueObjects, aspectRatio, lightDataList, ambientColor, ambientIntensity);
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
            _transparentPass.Execute(renderContext, camera, transparentObjects, lightDataList, ambientColor, ambientIntensity, aspectRatio);
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

        return HitTester3D.HitTest(screenPoint, Width, Height, _lastCamera, _lastObjects);
    }

    /// <summary>
    /// Performs hit testing and returns the path from root to the hit object.
    /// </summary>
    /// <param name="screenPoint">The point in screen coordinates.</param>
    /// <returns>A list representing the path from root to the hit object, or empty if none.</returns>
    public IReadOnlyList<Object3D.Resource> HitTestWithPath(Point screenPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_lastCamera == null || _lastObjects == null || _lastObjects.Count == 0)
            return [];

        return HitTester3D.HitTestWithPath(screenPoint, Width, Height, _lastCamera, _lastObjects);
    }

    public GizmoAxis GizmoHitTest(Point screenPoint, Object3D.Resource? gizmoTarget, GizmoMode gizmoMode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_lastCamera == null || gizmoTarget == null || gizmoMode == GizmoMode.None)
            return GizmoAxis.None;

        return GizmoHitTester.HitTest(
            screenPoint,
            Width,
            Height,
            _lastCamera,
            gizmoTarget.Position,
            gizmoTarget.Rotation,
            gizmoMode);
    }

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

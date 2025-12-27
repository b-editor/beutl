using System.Collections.Generic;
using System.Numerics;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Camera;
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
    private FlipPass? _flipPass;

    // Shadow management
    private ShadowManager? _shadowManager;

    // Final output for Skia integration
    private ISharedTexture? _outputTexture;

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

        // Create flip pass (corrects vertical orientation)
        _flipPass = new FlipPass(_context, _shaderCompiler);
        _flipPass.Initialize(width, height);

        // Create output texture for Skia integration
        _outputTexture = _context.CreateTexture(width, height, TextureFormat.BGRA8Unorm);
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

        // Resize flip pass
        _flipPass?.Resize(width, height);

        // Recreate output texture
        _outputTexture?.Dispose();
        _outputTexture = _context.CreateTexture(width, height, TextureFormat.BGRA8Unorm);
    }

    public void Render(
        Camera3D.Resource camera,
        IReadOnlyList<Object3D.Resource> objects,
        IReadOnlyList<Light3D.Resource> lights,
        Color backgroundColor,
        Color ambientColor,
        float ambientIntensity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_geometryPass == null || _lightingPass == null || _flipPass == null || _shadowManager == null)
            return;

        float aspectRatio = (float)Width / Height;

        // Calculate scene bounds for shadow mapping
        var (sceneCenter, sceneRadius) = CalculateSceneBounds(objects);

        // === SHADOW PASS ===
        var lightToShadowIndex = _shadowManager.RenderShadows(lights, objects, sceneCenter, sceneRadius);
        var shadowUbo = _shadowManager.GetShadowUBO();
        _shadowManager.PrepareForSampling();

        // Convert light resources to shader-compatible LightData
        var lightDataList = new List<LightData>();
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

        // === GEOMETRY PASS ===
        _geometryPass.Execute(camera, objects, aspectRatio, lightDataList, ambientColor, ambientIntensity);
        _geometryPass.PrepareForSampling();

        // === LIGHTING PASS ===
        _lightingPass.BindGBuffer(_geometryPass);
        _lightingPass.BindShadowMaps(_shadowManager);
        _lightingPass.Execute(camera, lightDataList, backgroundColor, ambientColor, ambientIntensity, shadowUbo);
        _lightingPass.PrepareForSampling();

        // === FLIP PASS ===
        _flipPass.SetInputTexture(_lightingPass.OutputTexture!);
        _flipPass.Execute();
        _flipPass.PrepareForSampling();

        // Copy result to output texture for Skia integration
        CopyToOutputTexture();
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

        // Copy from flip pass output (ITexture2D) to shared output texture (ISharedTexture)
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _flipPass?.Dispose();
        _lightingPass?.Dispose();
        _geometryPass?.Dispose();
        _shadowManager?.Dispose();
        _outputTexture?.Dispose();

        (_shaderCompiler as IDisposable)?.Dispose();
    }
}

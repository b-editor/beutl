using System.Collections.Generic;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Nodes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics3D;

/// <summary>
/// Deferred 3D renderer using G-Buffer for lighting calculations.
/// Coordinates the geometry and lighting passes.
/// </summary>
internal sealed class Renderer3D : I3DRenderer
{
    private readonly IGraphicsContext _context;
    private readonly IShaderCompiler _shaderCompiler;
    private bool _disposed;

    // Render passes
    private GeometryPass? _geometryPass;
    private LightingPass? _lightingPass;

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

        // Create geometry pass
        _geometryPass = new GeometryPass(_context, _shaderCompiler);
        _geometryPass.Initialize(width, height);

        // Create lighting pass (uses depth texture from geometry pass)
        _lightingPass = new LightingPass(_context, _shaderCompiler, _geometryPass.DepthTexture!);
        _lightingPass.Initialize(width, height);

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

        if (_geometryPass == null || _lightingPass == null)
            return;

        float aspectRatio = (float)Width / Height;

        // Convert light resources to shader-compatible LightData
        var lightDataList = new List<LightData>();
        foreach (var light in lights)
        {
            if (!light.IsLightEnabled)
                continue;

            lightDataList.Add(LightData.FromLight(light));

            if (lightDataList.Count >= RenderContext3D.MaxLights)
                break;
        }

        // === GEOMETRY PASS ===
        _geometryPass.Execute(camera, objects, aspectRatio, lightDataList, ambientColor, ambientIntensity);
        _geometryPass.PrepareForSampling();

        // === LIGHTING PASS ===
        _lightingPass.BindGBuffer(_geometryPass);
        _lightingPass.Execute(camera, lightDataList, backgroundColor, ambientColor, ambientIntensity);
        _lightingPass.PrepareForSampling();

        // Copy result to output texture for Skia integration
        CopyToOutputTexture();
    }

    private void CopyToOutputTexture()
    {
        if (_lightingPass?.OutputTexture == null || _outputTexture == null)
            return;

        // Copy from lighting output (ITexture2D) to shared output texture (ISharedTexture)
        _context.CopyTexture(_lightingPass.OutputTexture, _outputTexture);
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

        _lightingPass?.Dispose();
        _geometryPass?.Dispose();
        _outputTexture?.Dispose();

        (_shaderCompiler as IDisposable)?.Dispose();
    }
}

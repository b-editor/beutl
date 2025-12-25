using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Meshes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics3D;

/// <summary>
/// Backend-agnostic 3D renderer that uses abstracted graphics interfaces.
/// </summary>
internal sealed class Renderer3D : I3DRenderer
{
    private readonly IGraphicsContext _context;
    private readonly IRenderPass3D _renderPass;
    private readonly IShaderCompiler _shaderCompiler;
    private IFramebuffer3D? _currentFramebuffer;
    private ISharedTexture? _colorTexture;
    private bool _disposed;

    // Default material for objects without a material
    private readonly BasicMaterial _defaultMaterial = new();
    private BasicMaterial.Resource? _defaultMaterialResource;

    public Renderer3D(IGraphicsContext context)
    {
        _context = context;
        _renderPass = context.CreateRenderPass3D();
        _shaderCompiler = context.CreateShaderCompiler();
    }

    public int Width { get; private set; }
    public int Height { get; private set; }

    public void Initialize(int width, int height)
    {
        Width = width;
        Height = height;

        // Create initial framebuffer
        CreateFramebuffer(width, height);

        // Initialize default material
        _defaultMaterialResource = (BasicMaterial.Resource)_defaultMaterial.ToResource(new RenderContext(TimeSpan.Zero));
    }

    private void CreateFramebuffer(int width, int height)
    {
        _currentFramebuffer?.Dispose();
        _colorTexture?.Dispose();

        _colorTexture = _context.CreateTexture(width, height, TextureFormat.BGRA8Unorm);
        _currentFramebuffer = _context.CreateFramebuffer3D(_renderPass, _colorTexture);
    }

    public void Resize(int width, int height)
    {
        if (Width == width && Height == height)
            return;

        Width = width;
        Height = height;
        CreateFramebuffer(width, height);
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

        if (_currentFramebuffer == null)
            return;

        float aspectRatio = (float)Width / Height;

        // Get the first directional light (simplified for now)
        Vector3 lightDirection = new(0, -1, -1);
        Vector3 lightColor = new(1, 1, 1);

        foreach (var light in lights)
        {
            if (light is DirectionalLight3D.Resource dirLight && dirLight.IsLightEnabled)
            {
                var normalizedDir = dirLight.Direction;
                if (normalizedDir != Vector3.Zero)
                    normalizedDir = Vector3.Normalize(normalizedDir);
                lightDirection = normalizedDir;
                var color = dirLight.Color;
                lightColor = new Vector3(color.R / 255f, color.G / 255f, color.B / 255f) * dirLight.Intensity;
                break;
            }
        }

        // Create render context for materials
        var renderContext = new RenderContext3D(
            _context,
            _renderPass,
            _shaderCompiler,
            camera.GetViewMatrix(),
            camera.GetProjectionMatrix(aspectRatio),
            camera.Position,
            lightDirection,
            lightColor,
            new Vector3(ambientColor.R / 255f, ambientColor.G / 255f, ambientColor.B / 255f) * ambientIntensity);

        // Begin render pass
        _renderPass.Begin(_currentFramebuffer, backgroundColor);

        // Render each object
        foreach (var obj in objects)
        {
            if (!obj.IsVisible)
                continue;

            // Get mesh resource from object
            var meshResource = obj.GetMesh();
            if (meshResource == null)
                continue;

            // Ensure GPU buffers are created/updated
            EnsureMeshBuffers(meshResource);

            if (meshResource.VertexBuffer == null || meshResource.IndexBuffer == null)
                continue;

            // Get material resource (use default if not set)
            var materialResource = obj.Material as Material3D.Resource ?? _defaultMaterialResource;
            if (materialResource == null)
                continue;

            // Ensure material pipeline is created
            materialResource.EnsurePipeline(renderContext);

            // Bind material (pipeline, uniforms, descriptor sets)
            materialResource.Bind(renderContext, obj);

            // Bind vertex and index buffers
            _renderPass.BindVertexBuffer(meshResource.VertexBuffer);
            _renderPass.BindIndexBuffer(meshResource.IndexBuffer);

            // Draw the mesh
            _renderPass.DrawIndexed((uint)meshResource.IndexCount);
        }

        _renderPass.End();

        // Transition color texture for sampling
        _currentFramebuffer.ColorTexture.PrepareForSampling();
    }

    private void EnsureMeshBuffers(Mesh.Resource meshResource)
    {
        if (!meshResource.BuffersDirty)
            return;

        var vertices = meshResource.GetVertices();
        var indices = meshResource.GetIndices();

        if (vertices.Length == 0 || indices.Length == 0)
            return;

        ulong vertexSize = (ulong)(vertices.Length * Marshal.SizeOf<Vertex3D>());
        ulong indexSize = (ulong)(indices.Length * sizeof(uint));

        // Dispose old buffers if they exist
        meshResource.VertexBuffer?.Dispose();
        meshResource.IndexBuffer?.Dispose();

        // Create new device-local buffers
        var vertexBuffer = _context.CreateBuffer(
            vertexSize,
            BufferUsage.VertexBuffer | BufferUsage.TransferDestination,
            MemoryProperty.DeviceLocal);

        var indexBuffer = _context.CreateBuffer(
            indexSize,
            BufferUsage.IndexBuffer | BufferUsage.TransferDestination,
            MemoryProperty.DeviceLocal);

        // Create staging buffers and upload
        using var vertexStaging = _context.CreateBuffer(
            vertexSize,
            BufferUsage.TransferSource,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        using var indexStaging = _context.CreateBuffer(
            indexSize,
            BufferUsage.TransferSource,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        vertexStaging.Upload(vertices);
        indexStaging.Upload(indices);

        // Copy from staging to device local
        _context.CopyBuffer(vertexStaging, vertexBuffer, vertexSize);
        _context.CopyBuffer(indexStaging, indexBuffer, indexSize);

        // Store in mesh resource
        meshResource.VertexBuffer = vertexBuffer;
        meshResource.IndexBuffer = indexBuffer;
        meshResource.BuffersDirty = false;
    }

    public SKSurface? CreateSkiaSurface()
    {
        return _colorTexture?.CreateSkiaSurface();
    }

    public byte[] DownloadPixels()
    {
        return _colorTexture?.DownloadPixels() ?? [];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _defaultMaterialResource?.Dispose();
        _currentFramebuffer?.Dispose();
        _colorTexture?.Dispose();
        (_shaderCompiler as IDisposable)?.Dispose();
        _renderPass.Dispose();
    }
}

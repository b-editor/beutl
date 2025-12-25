using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
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
    private IPipeline3D? _basicPipeline;
    private IFramebuffer3D? _currentFramebuffer;
    private ISharedTexture? _colorTexture;
    private IBuffer? _uniformBuffer;
    private IDescriptorSet? _descriptorSet;
    private bool _disposed;

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

        // Create uniform buffer for MVP matrix
        _uniformBuffer = _context.CreateBuffer(
            (ulong)Marshal.SizeOf<UniformBufferObject>(),
            BufferUsage.UniformBuffer,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        // Create basic shaders and pipeline
        CreateBasicPipeline();

        // Create initial framebuffer
        CreateFramebuffer(width, height);
    }

    private void CreateBasicPipeline()
    {
        var vertexSpirv = _shaderCompiler.CompileToSpirv(BasicShaderSources.VertexShader, ShaderStage.Vertex);
        var fragmentSpirv = _shaderCompiler.CompileToSpirv(BasicShaderSources.FragmentShader, ShaderStage.Fragment);

        var descriptorBindings = new DescriptorBinding[]
        {
            new(0, DescriptorType.UniformBuffer, 1, ShaderStage.Vertex | ShaderStage.Fragment)
        };

        _basicPipeline = _context.CreatePipeline3D(
            _renderPass,
            vertexSpirv,
            fragmentSpirv,
            descriptorBindings);

        // Create descriptor set
        var poolSizes = new DescriptorPoolSize[]
        {
            new(DescriptorType.UniformBuffer, 1)
        };

        _descriptorSet = _context.CreateDescriptorSet(_basicPipeline, poolSizes);
        _descriptorSet.UpdateBuffer(0, _uniformBuffer!);
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

        if (_currentFramebuffer == null || _basicPipeline == null)
            return;

        float aspectRatio = (float)Width / Height;

        // Begin render pass
        _renderPass.Begin(_currentFramebuffer, backgroundColor);

        // Bind pipeline
        _renderPass.BindPipeline(_basicPipeline);

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

            // Update uniform buffer
            var ubo = new UniformBufferObject
            {
                Model = obj.GetWorldMatrix(),
                View = camera.GetViewMatrix(),
                Projection = camera.GetProjectionMatrix(aspectRatio),
                LightDirection = lightDirection,
                LightColor = lightColor,
                AmbientColor = new Vector3(ambientColor.R / 255f, ambientColor.G / 255f, ambientColor.B / 255f) * ambientIntensity,
                ViewPosition = camera.Position
            };

            // Get material color
            if (obj.Material is BasicMaterial.Resource basicMat)
            {
                var diffuse = basicMat.DiffuseColor;
                ubo.ObjectColor = new Vector4(diffuse.R / 255f, diffuse.G / 255f, diffuse.B / 255f, diffuse.A / 255f);
            }
            else
            {
                ubo.ObjectColor = new Vector4(1, 1, 1, 1);
            }

            _uniformBuffer!.Upload(new ReadOnlySpan<UniformBufferObject>(ref ubo));

            // Bind vertex and index buffers
            _renderPass.BindVertexBuffer(meshResource.VertexBuffer);
            _renderPass.BindIndexBuffer(meshResource.IndexBuffer);

            // Bind descriptor set
            _renderPass.BindDescriptorSet(_basicPipeline, _descriptorSet!);

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

        _descriptorSet?.Dispose();
        _uniformBuffer?.Dispose();
        _basicPipeline?.Dispose();
        _currentFramebuffer?.Dispose();
        _colorTexture?.Dispose();
        (_shaderCompiler as IDisposable)?.Dispose();
        _renderPass.Dispose();
    }
}

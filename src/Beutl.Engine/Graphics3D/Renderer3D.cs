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
    private readonly Dictionary<Mesh, (IBuffer vertex, IBuffer index)> _meshBuffers = new();
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
        var vertexShaderSource = GetBasicVertexShader();
        var fragmentShaderSource = GetBasicFragmentShader();

        var vertexSpirv = _shaderCompiler.CompileToSpirv(vertexShaderSource, ShaderStage.Vertex);
        var fragmentSpirv = _shaderCompiler.CompileToSpirv(fragmentShaderSource, ShaderStage.Fragment);

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
        IReadOnlyList<(Object3D.Resource obj, Mesh mesh)> objects,
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
        foreach (var (obj, mesh) in objects)
        {
            if (!obj.IsVisible)
                continue;

            // Get or create mesh buffers
            if (!_meshBuffers.TryGetValue(mesh, out var buffers))
            {
                buffers = CreateMeshBuffers(mesh);
                _meshBuffers[mesh] = buffers;
            }

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
            _renderPass.BindVertexBuffer(buffers.vertex);
            _renderPass.BindIndexBuffer(buffers.index);

            // Bind descriptor set
            _renderPass.BindDescriptorSet(_basicPipeline, _descriptorSet!);

            // Draw the mesh
            _renderPass.DrawIndexed((uint)mesh.Indices.Length);
        }

        _renderPass.End();

        // Transition color texture for sampling
        _currentFramebuffer.ColorTexture.PrepareForSampling();
    }

    private (IBuffer vertex, IBuffer index) CreateMeshBuffers(Mesh mesh)
    {
        var vertices = mesh.Vertices;
        var indices = mesh.Indices;
        int vertexCount = vertices.Length;
        int indexCount = indices.Length;
        ulong vertexSize = (ulong)(vertexCount * Marshal.SizeOf<Vertex3D>());
        ulong indexSize = (ulong)(indexCount * sizeof(uint));

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

        return (vertexBuffer, indexBuffer);
    }

    public SKSurface? CreateSkiaSurface()
    {
        return _colorTexture?.CreateSkiaSurface();
    }

    public byte[] DownloadPixels()
    {
        return _colorTexture?.DownloadPixels() ?? [];
    }

    private static string GetBasicVertexShader() => """
        #version 450

        layout(location = 0) in vec3 inPosition;
        layout(location = 1) in vec3 inNormal;
        layout(location = 2) in vec2 inTexCoord;

        layout(binding = 0) uniform UniformBufferObject {
            mat4 model;
            mat4 view;
            mat4 projection;
            vec3 lightDirection;
            float _pad1;
            vec3 lightColor;
            float _pad2;
            vec3 ambientColor;
            float _pad3;
            vec3 viewPosition;
            float _pad4;
            vec4 objectColor;
        } ubo;

        layout(location = 0) out vec3 fragNormal;
        layout(location = 1) out vec3 fragPosition;
        layout(location = 2) out vec2 fragTexCoord;

        void main() {
            vec4 worldPos = ubo.model * vec4(inPosition, 1.0);
            gl_Position = ubo.projection * ubo.view * worldPos;

            fragNormal = mat3(transpose(inverse(ubo.model))) * inNormal;
            fragPosition = worldPos.xyz;
            fragTexCoord = inTexCoord;
        }
        """;

    private static string GetBasicFragmentShader() => """
        #version 450

        layout(location = 0) in vec3 fragNormal;
        layout(location = 1) in vec3 fragPosition;
        layout(location = 2) in vec2 fragTexCoord;

        layout(binding = 0) uniform UniformBufferObject {
            mat4 model;
            mat4 view;
            mat4 projection;
            vec3 lightDirection;
            float _pad1;
            vec3 lightColor;
            float _pad2;
            vec3 ambientColor;
            float _pad3;
            vec3 viewPosition;
            float _pad4;
            vec4 objectColor;
        } ubo;

        layout(location = 0) out vec4 outColor;

        void main() {
            vec3 normal = normalize(fragNormal);
            vec3 lightDir = normalize(-ubo.lightDirection);

            // Ambient
            vec3 ambient = ubo.ambientColor * ubo.objectColor.rgb;

            // Diffuse
            float diff = max(dot(normal, lightDir), 0.0);
            vec3 diffuse = diff * ubo.lightColor * ubo.objectColor.rgb;

            // Simple specular (Blinn-Phong)
            vec3 viewDir = normalize(ubo.viewPosition - fragPosition);
            vec3 halfwayDir = normalize(lightDir + viewDir);
            float spec = pow(max(dot(normal, halfwayDir), 0.0), 32.0);
            vec3 specular = spec * ubo.lightColor * 0.5;

            vec3 result = ambient + diffuse + specular;
            outColor = vec4(result, ubo.objectColor.a);
        }
        """;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var (_, buffers) in _meshBuffers)
        {
            buffers.vertex.Dispose();
            buffers.index.Dispose();
        }
        _meshBuffers.Clear();

        _descriptorSet?.Dispose();
        _uniformBuffer?.Dispose();
        _basicPipeline?.Dispose();
        _currentFramebuffer?.Dispose();
        _colorTexture?.Dispose();
        (_shaderCompiler as IDisposable)?.Dispose();
        _renderPass.Dispose();
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct UniformBufferObject
{
    public Matrix4x4 Model;
    public Matrix4x4 View;
    public Matrix4x4 Projection;
    public Vector3 LightDirection;
    private float _pad1;
    public Vector3 LightColor;
    private float _pad2;
    public Vector3 AmbientColor;
    private float _pad3;
    public Vector3 ViewPosition;
    private float _pad4;
    public Vector4 ObjectColor;
}

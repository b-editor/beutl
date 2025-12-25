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
/// Deferred 3D renderer using G-Buffer for lighting calculations.
/// </summary>
internal sealed class Renderer3D : I3DRenderer
{
    private readonly IGraphicsContext _context;
    private readonly IShaderCompiler _shaderCompiler;
    private bool _disposed;

    // G-Buffer textures
    private ITexture2D? _positionTexture;      // World position (RGB16F)
    private ITexture2D? _normalMetallicTexture; // Normal (RGB) + Metallic (A) (RGBA16F)
    private ITexture2D? _albedoRoughnessTexture; // Albedo (RGB) + Roughness (A) (RGBA8)
    private ITexture2D? _emissionAOTexture;     // Emission (RGB) + AO (A) (RGBA16F)
    private ITexture2D? _depthTexture;          // Depth (D32F)

    // Geometry pass resources
    private IRenderPass3D? _geometryPass;
    private IFramebuffer3D? _geometryFramebuffer;

    // Lighting pass resources
    private IRenderPass3D? _lightingPass;
    private IFramebuffer3D? _lightingFramebuffer;
    private ITexture2D? _lightingOutputTexture;
    private IPipeline3D? _lightingPipeline;
    private IDescriptorSet? _lightingDescriptorSet;
    private IBuffer? _lightingUniformBuffer;
    private IBuffer? _lightsBuffer;

    // Final output for Skia integration
    private ISharedTexture? _outputTexture;

    // Default material for objects without a material
    private readonly BasicMaterial _defaultMaterial = new();
    private BasicMaterial.Resource? _defaultMaterialResource;

    // G-Buffer formats
    private static readonly TextureFormat[] GBufferFormats =
    [
        TextureFormat.RGBA16Float,  // Position
        TextureFormat.RGBA16Float,  // Normal + Metallic
        TextureFormat.RGBA8Unorm,   // Albedo + Roughness
        TextureFormat.RGBA16Float   // Emission + AO
    ];

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

        CreateGBuffer(width, height);
        CreateLightingPass(width, height);

        // Initialize default material
        _defaultMaterialResource = (BasicMaterial.Resource)_defaultMaterial.ToResource(new RenderContext(TimeSpan.Zero));
    }

    private void CreateGBuffer(int width, int height)
    {
        // Dispose old resources
        _geometryFramebuffer?.Dispose();
        _geometryPass?.Dispose();
        _positionTexture?.Dispose();
        _normalMetallicTexture?.Dispose();
        _albedoRoughnessTexture?.Dispose();
        _emissionAOTexture?.Dispose();
        _depthTexture?.Dispose();

        // Create G-Buffer textures
        _positionTexture = _context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        _normalMetallicTexture = _context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        _albedoRoughnessTexture = _context.CreateTexture2D(width, height, TextureFormat.RGBA8Unorm);
        _emissionAOTexture = _context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        _depthTexture = _context.CreateTexture2D(width, height, TextureFormat.Depth32Float);

        // Create geometry render pass and framebuffer
        _geometryPass = _context.CreateRenderPass3D(GBufferFormats, TextureFormat.Depth32Float);
        _geometryFramebuffer = _context.CreateFramebuffer3D(
            _geometryPass,
            new[] { _positionTexture, _normalMetallicTexture, _albedoRoughnessTexture, _emissionAOTexture },
            _depthTexture);
    }

    private void CreateLightingPass(int width, int height)
    {
        // Dispose old resources
        _lightingFramebuffer?.Dispose();
        _lightingPass?.Dispose();
        _lightingOutputTexture?.Dispose();
        _outputTexture?.Dispose();
        _lightingPipeline?.Dispose();
        _lightingDescriptorSet?.Dispose();
        _lightingUniformBuffer?.Dispose();
        _lightsBuffer?.Dispose();

        // Create output textures
        _lightingOutputTexture = _context.CreateTexture2D(width, height, TextureFormat.RGBA8Unorm);
        _outputTexture = _context.CreateTexture(width, height, TextureFormat.BGRA8Unorm);

        // Create lighting render pass and framebuffer (single color attachment)
        _lightingPass = _context.CreateRenderPass3D([TextureFormat.RGBA8Unorm], TextureFormat.Depth32Float);

        // Reuse depth texture from geometry pass
        _lightingFramebuffer = _context.CreateFramebuffer3D(
            _lightingPass,
            [_lightingOutputTexture],
            _depthTexture!);

        // Create lighting pipeline
        CreateLightingPipeline();
    }

    private void CreateLightingPipeline()
    {
        // Create uniform buffers
        _lightingUniformBuffer = _context.CreateBuffer(
            (ulong)Marshal.SizeOf<LightingPassUBO>(),
            BufferUsage.UniformBuffer,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        _lightsBuffer = _context.CreateBuffer(
            (ulong)(Marshal.SizeOf<LightData>() * RenderContext3D.MaxLights + 16),
            BufferUsage.UniformBuffer,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        // Compile shaders
        var vertexSpirv = _shaderCompiler.CompileToSpirv(LightingVertexShader, ShaderStage.Vertex);
        var fragmentSpirv = _shaderCompiler.CompileToSpirv(LightingFragmentShader, ShaderStage.Fragment);

        // Descriptor bindings: 4 G-Buffer samplers + 2 uniform buffers
        var descriptorBindings = new DescriptorBinding[]
        {
            new(0, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment), // Position
            new(1, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment), // Normal+Metallic
            new(2, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment), // Albedo+Roughness
            new(3, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment), // Emission+AO
            new(4, DescriptorType.UniformBuffer, 1, ShaderStage.Fragment),        // Camera/ambient
            new(5, DescriptorType.UniformBuffer, 1, ShaderStage.Fragment)         // Lights
        };

        _lightingPipeline = _context.CreatePipeline3D(
            _lightingPass!,
            vertexSpirv,
            fragmentSpirv,
            descriptorBindings);

        // Create descriptor set
        var poolSizes = new DescriptorPoolSize[]
        {
            new(DescriptorType.CombinedImageSampler, 4),
            new(DescriptorType.UniformBuffer, 2)
        };

        _lightingDescriptorSet = _context.CreateDescriptorSet(_lightingPipeline, poolSizes);
        _lightingDescriptorSet.UpdateBuffer(4, _lightingUniformBuffer);
        _lightingDescriptorSet.UpdateBuffer(5, _lightsBuffer);
    }

    public void Resize(int width, int height)
    {
        if (Width == width && Height == height)
            return;

        Width = width;
        Height = height;
        CreateGBuffer(width, height);
        CreateLightingPass(width, height);
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

        if (_geometryFramebuffer == null || _lightingFramebuffer == null)
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
        RenderGeometryPass(camera, objects, aspectRatio, lightDataList, ambientColor, ambientIntensity);

        // === LIGHTING PASS ===
        RenderLightingPass(camera, lightDataList, backgroundColor, ambientColor, ambientIntensity);

        // Copy result to output texture for Skia integration
        CopyToOutputTexture();
    }

    private void RenderGeometryPass(
        Camera3D.Resource camera,
        IReadOnlyList<Object3D.Resource> objects,
        float aspectRatio,
        List<LightData> lightDataList,
        Color ambientColor,
        float ambientIntensity)
    {
        // Create render context for materials
        var renderContext = new RenderContext3D(
            _context,
            _geometryPass!,
            _shaderCompiler,
            camera.GetViewMatrix(),
            camera.GetProjectionMatrix(aspectRatio),
            camera.Position,
            new Vector3(ambientColor.R / 255f, ambientColor.G / 255f, ambientColor.B / 255f) * ambientIntensity,
            lightDataList);

        // Clear colors for G-Buffer (black/zero for most, except normal which should be (0,0,1) for up)
        Span<Color> clearColors =
        [
            new Color(255, 0, 0, 0),    // Position (black, alpha=1 for valid)
            new Color(255, 128, 128, 255), // Normal (0.5,0.5,1 = up normal) + Metallic=1
            new Color(255, 0, 0, 0),    // Albedo + Roughness
            new Color(255, 0, 0, 0)     // Emission + AO
        ];

        // Begin geometry pass
        _geometryPass!.Begin(_geometryFramebuffer!, clearColors);

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
            _geometryPass.BindVertexBuffer(meshResource.VertexBuffer);
            _geometryPass.BindIndexBuffer(meshResource.IndexBuffer);

            // Draw the mesh
            _geometryPass.DrawIndexed((uint)meshResource.IndexCount);
        }

        _geometryPass.End();

        // Prepare G-Buffer for sampling
        _geometryFramebuffer!.PrepareForSampling();
    }

    private void RenderLightingPass(
        Camera3D.Resource camera,
        List<LightData> lightDataList,
        Color backgroundColor,
        Color ambientColor,
        float ambientIntensity)
    {
        // Update lighting uniform buffer
        var ubo = new LightingPassUBO
        {
            CameraPosition = camera.Position,
            AmbientColor = new Vector3(
                ambientColor.R / 255f * ambientIntensity,
                ambientColor.G / 255f * ambientIntensity,
                ambientColor.B / 255f * ambientIntensity)
        };
        _lightingUniformBuffer!.Upload(new ReadOnlySpan<LightingPassUBO>(ref ubo));

        // Update lights buffer
        var lightsData = new LightsBufferData { LightCount = lightDataList.Count };
        for (int i = 0; i < Math.Min(lightDataList.Count, RenderContext3D.MaxLights); i++)
        {
            lightsData.SetLight(i, lightDataList[i]);
        }
        _lightsBuffer!.Upload(new ReadOnlySpan<LightsBufferData>(ref lightsData));

        // Update G-Buffer texture bindings in descriptor set
        _lightingDescriptorSet!.UpdateTexture(0, _positionTexture!);
        _lightingDescriptorSet.UpdateTexture(1, _normalMetallicTexture!);
        _lightingDescriptorSet.UpdateTexture(2, _albedoRoughnessTexture!);
        _lightingDescriptorSet.UpdateTexture(3, _emissionAOTexture!);

        // Begin lighting pass
        Span<Color> clearColors = [backgroundColor];
        _lightingPass!.Begin(_lightingFramebuffer!, clearColors, 1.0f);

        // Bind lighting pipeline and descriptor set
        _lightingPass.BindPipeline(_lightingPipeline!);
        _lightingPass.BindDescriptorSet(_lightingPipeline!, _lightingDescriptorSet);

        // Draw fullscreen triangle
        _lightingPass.Draw(3);

        _lightingPass.End();

        // Prepare for sampling
        _lightingFramebuffer!.PrepareForSampling();
    }

    private void CopyToOutputTexture()
    {
        // TODO: Copy from _lightingOutputTexture to _outputTexture
        // For now, the lighting pass output is in _lightingOutputTexture
        // We need to blit or copy it to the shared texture for Skia
        _outputTexture?.PrepareForRender();
        // The actual copy would need a blit command or another render pass
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

        _defaultMaterialResource?.Dispose();

        // Lighting pass resources
        _lightingDescriptorSet?.Dispose();
        _lightingPipeline?.Dispose();
        _lightsBuffer?.Dispose();
        _lightingUniformBuffer?.Dispose();
        _lightingFramebuffer?.Dispose();
        _lightingPass?.Dispose();
        _lightingOutputTexture?.Dispose();

        // G-Buffer resources
        _geometryFramebuffer?.Dispose();
        _geometryPass?.Dispose();
        _positionTexture?.Dispose();
        _normalMetallicTexture?.Dispose();
        _albedoRoughnessTexture?.Dispose();
        _emissionAOTexture?.Dispose();
        _depthTexture?.Dispose();

        // Output
        _outputTexture?.Dispose();

        (_shaderCompiler as IDisposable)?.Dispose();
    }

    // === Lighting Pass Shader ===

    private static string LightingVertexShader => """
        #version 450

        // Fullscreen triangle - no vertex input needed
        layout(location = 0) out vec2 fragTexCoord;

        void main() {
            // Generate fullscreen triangle vertices
            vec2 positions[3] = vec2[](
                vec2(-1.0, -1.0),
                vec2(3.0, -1.0),
                vec2(-1.0, 3.0)
            );

            vec2 texCoords[3] = vec2[](
                vec2(0.0, 0.0),
                vec2(2.0, 0.0),
                vec2(0.0, 2.0)
            );

            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            fragTexCoord = texCoords[gl_VertexIndex];
        }
        """;

    private static string LightingFragmentShader => """
        #version 450

        const float PI = 3.14159265359;
        const int MAX_LIGHTS = 8;

        // Light types
        const int LIGHT_DIRECTIONAL = 0;
        const int LIGHT_POINT = 1;
        const int LIGHT_SPOT = 2;

        struct Light {
            vec3 positionOrDirection;
            int type;
            vec3 color;
            float intensity;
            vec3 direction;
            float range;
            float constantAtt;
            float linearAtt;
            float quadraticAtt;
            float innerCutoff;
            float outerCutoff;
            float _pad;
        };

        layout(location = 0) in vec2 fragTexCoord;

        layout(binding = 0) uniform sampler2D gPosition;
        layout(binding = 1) uniform sampler2D gNormalMetallic;
        layout(binding = 2) uniform sampler2D gAlbedoRoughness;
        layout(binding = 3) uniform sampler2D gEmissionAO;

        layout(binding = 4) uniform CameraUBO {
            vec3 cameraPosition;
            float _pad1;
            vec3 ambientColor;
            float _pad2;
        } camera;

        layout(binding = 5) uniform LightsUBO {
            int lightCount;
            int _pad1;
            int _pad2;
            int _pad3;
            Light lights[MAX_LIGHTS];
        } lighting;

        layout(location = 0) out vec4 outColor;

        // PBR functions
        float DistributionGGX(vec3 N, vec3 H, float roughness) {
            float a = roughness * roughness;
            float a2 = a * a;
            float NdotH = max(dot(N, H), 0.0);
            float NdotH2 = NdotH * NdotH;
            float num = a2;
            float denom = (NdotH2 * (a2 - 1.0) + 1.0);
            denom = PI * denom * denom;
            return num / denom;
        }

        float GeometrySchlickGGX(float NdotV, float roughness) {
            float r = (roughness + 1.0);
            float k = (r * r) / 8.0;
            float num = NdotV;
            float denom = NdotV * (1.0 - k) + k;
            return num / denom;
        }

        float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness) {
            float NdotV = max(dot(N, V), 0.0);
            float NdotL = max(dot(N, L), 0.0);
            float ggx2 = GeometrySchlickGGX(NdotV, roughness);
            float ggx1 = GeometrySchlickGGX(NdotL, roughness);
            return ggx1 * ggx2;
        }

        vec3 fresnelSchlick(float cosTheta, vec3 F0) {
            return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
        }

        float calculateAttenuation(float distance, float constant, float linear, float quadratic) {
            return 1.0 / (constant + linear * distance + quadratic * distance * distance);
        }

        vec3 calculateLight(Light light, vec3 worldPos, vec3 N, vec3 V, vec3 F0, vec3 albedo, float metallic, float roughness) {
            vec3 L;
            float attenuation = 1.0;
            float spotEffect = 1.0;

            if (light.type == LIGHT_DIRECTIONAL) {
                L = normalize(-light.positionOrDirection);
            } else {
                vec3 lightToFrag = light.positionOrDirection - worldPos;
                float distance = length(lightToFrag);
                if (distance > light.range) return vec3(0.0);
                L = normalize(lightToFrag);
                attenuation = calculateAttenuation(distance, light.constantAtt, light.linearAtt, light.quadraticAtt);

                if (light.type == LIGHT_SPOT) {
                    float theta = dot(L, normalize(-light.direction));
                    float epsilon = light.innerCutoff - light.outerCutoff;
                    spotEffect = clamp((theta - light.outerCutoff) / epsilon, 0.0, 1.0);
                }
            }

            vec3 H = normalize(V + L);
            vec3 radiance = light.color * light.intensity * attenuation * spotEffect;

            float NDF = DistributionGGX(N, H, roughness);
            float G = GeometrySmith(N, V, L, roughness);
            vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);

            vec3 kS = F;
            vec3 kD = vec3(1.0) - kS;
            kD *= 1.0 - metallic;

            vec3 numerator = NDF * G * F;
            float denominator = 4.0 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0) + 0.0001;
            vec3 specular = numerator / denominator;

            float NdotL = max(dot(N, L), 0.0);
            return (kD * albedo / PI + specular) * radiance * NdotL;
        }

        void main() {
            // Sample G-Buffer
            vec4 positionSample = texture(gPosition, fragTexCoord);
            vec4 normalMetallicSample = texture(gNormalMetallic, fragTexCoord);
            vec4 albedoRoughnessSample = texture(gAlbedoRoughness, fragTexCoord);
            vec4 emissionAOSample = texture(gEmissionAO, fragTexCoord);

            // Discard background pixels (where no geometry was rendered)
            if (positionSample.a < 0.5) {
                discard;
            }

            vec3 worldPos = positionSample.rgb;
            vec3 N = normalize(normalMetallicSample.rgb * 2.0 - 1.0);
            float metallic = normalMetallicSample.a;
            vec3 albedo = albedoRoughnessSample.rgb;
            float roughness = max(albedoRoughnessSample.a, 0.04);
            vec3 emission = emissionAOSample.rgb;
            float ao = emissionAOSample.a;

            vec3 V = normalize(camera.cameraPosition - worldPos);

            // Calculate reflectance at normal incidence
            vec3 F0 = vec3(0.04);
            F0 = mix(F0, albedo, metallic);

            // Accumulate lighting
            vec3 Lo = vec3(0.0);
            for (int i = 0; i < lighting.lightCount && i < MAX_LIGHTS; i++) {
                Lo += calculateLight(lighting.lights[i], worldPos, N, V, F0, albedo, metallic, roughness);
            }

            // Ambient lighting
            vec3 ambient = camera.ambientColor * albedo * ao;

            // Final color
            vec3 color = ambient + Lo + emission;

            // Tone mapping (Reinhard)
            color = color / (color + vec3(1.0));

            // Gamma correction
            color = pow(color, vec3(1.0 / 2.2));

            outColor = vec4(color, 1.0);
        }
        """;

    // === UBO Structs ===

    [StructLayout(LayoutKind.Sequential)]
    private struct LightingPassUBO
    {
        public Vector3 CameraPosition;
        private float _pad1;
        public Vector3 AmbientColor;
        private float _pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LightsBufferData
    {
        public int LightCount;
        private int _pad1;
        private int _pad2;
        private int _pad3;
        private LightData _light0;
        private LightData _light1;
        private LightData _light2;
        private LightData _light3;
        private LightData _light4;
        private LightData _light5;
        private LightData _light6;
        private LightData _light7;

        public void SetLight(int index, LightData light)
        {
            switch (index)
            {
                case 0: _light0 = light; break;
                case 1: _light1 = light; break;
                case 2: _light2 = light; break;
                case 3: _light3 = light; break;
                case 4: _light4 = light; break;
                case 5: _light5 = light; break;
                case 6: _light6 = light; break;
                case 7: _light7 = light; break;
            }
        }
    }
}

using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Lighting;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Lighting pass for deferred rendering.
/// Samples the G-Buffer and applies PBR lighting to produce the final lit image.
/// </summary>
public sealed class LightingPass : GraphicsNode3D
{
    private readonly ITexture2D _depthTexture;

    private IPipeline3D? _pipeline;
    private IDescriptorSet? _descriptorSet;
    private IBuffer? _cameraUniformBuffer;
    private IBuffer? _lightsBuffer;
    private ISampler? _gBufferSampler;

    public LightingPass(IGraphicsContext context, IShaderCompiler shaderCompiler, ITexture2D depthTexture)
        : base(context, shaderCompiler)
    {
        _depthTexture = depthTexture ?? throw new ArgumentNullException(nameof(depthTexture));
    }

    /// <summary>
    /// Gets the output texture containing the lit scene.
    /// </summary>
    public ITexture2D? OutputTexture { get; private set; }

    protected override void OnInitialize(int width, int height)
    {
        CreateLightingResources(width, height);
    }

    protected override void OnResize(int width, int height)
    {
        CreateLightingResources(width, height);
    }

    private void CreateLightingResources(int width, int height)
    {
        // Dispose old resources
        Framebuffer?.Dispose();
        RenderPass?.Dispose();
        OutputTexture?.Dispose();
        _pipeline?.Dispose();
        _descriptorSet?.Dispose();
        _cameraUniformBuffer?.Dispose();
        _lightsBuffer?.Dispose();
        _gBufferSampler?.Dispose();

        // Create output texture
        OutputTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA8Unorm);

        // Create lighting render pass and framebuffer (single color attachment)
        RenderPass = Context.CreateRenderPass3D([TextureFormat.RGBA8Unorm], TextureFormat.Depth32Float);

        // Reuse depth texture from geometry pass
        Framebuffer = Context.CreateFramebuffer3D(
            RenderPass,
            [OutputTexture],
            _depthTexture);

        // Create lighting pipeline
        CreateLightingPipeline();
    }

    private void CreateLightingPipeline()
    {
        // Create sampler for G-Buffer textures
        _gBufferSampler = Context.CreateSampler(
            SamplerFilter.Nearest,
            SamplerFilter.Nearest,
            SamplerAddressMode.ClampToEdge,
            SamplerAddressMode.ClampToEdge);

        // Create uniform buffers
        _cameraUniformBuffer = Context.CreateBuffer(
            (ulong)Marshal.SizeOf<LightingPassUBO>(),
            BufferUsage.UniformBuffer,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        _lightsBuffer = Context.CreateBuffer(
            (ulong)(Marshal.SizeOf<LightData>() * RenderContext3D.MaxLights + 16),
            BufferUsage.UniformBuffer,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        // Compile shaders
        var vertexSpirv = ShaderCompiler.CompileToSpirv(LightingVertexShader, ShaderStage.Vertex);
        var fragmentSpirv = ShaderCompiler.CompileToSpirv(LightingFragmentShader, ShaderStage.Fragment);

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

        // Use fullscreen pipeline (no vertex input, no depth test)
        _pipeline = Context.CreatePipeline3D(
            RenderPass!,
            vertexSpirv,
            fragmentSpirv,
            descriptorBindings,
            VertexInputDescription.Empty,
            PipelineOptions.Fullscreen);

        // Create descriptor set
        var poolSizes = new DescriptorPoolSize[]
        {
            new(DescriptorType.CombinedImageSampler, 4),
            new(DescriptorType.UniformBuffer, 2)
        };

        _descriptorSet = Context.CreateDescriptorSet(_pipeline, poolSizes);
        _descriptorSet.UpdateBuffer(4, _cameraUniformBuffer);
        _descriptorSet.UpdateBuffer(5, _lightsBuffer);
    }

    /// <summary>
    /// Binds the G-Buffer textures from the geometry pass.
    /// </summary>
    public void BindGBuffer(GeometryPass geometryPass)
    {
        if (_descriptorSet == null || _gBufferSampler == null)
            return;

        _descriptorSet.UpdateTexture(0, geometryPass.PositionTexture!, _gBufferSampler);
        _descriptorSet.UpdateTexture(1, geometryPass.NormalMetallicTexture!, _gBufferSampler);
        _descriptorSet.UpdateTexture(2, geometryPass.AlbedoRoughnessTexture!, _gBufferSampler);
        _descriptorSet.UpdateTexture(3, geometryPass.EmissionAOTexture!, _gBufferSampler);
    }

    /// <summary>
    /// Executes the lighting pass.
    /// </summary>
    public void Execute(
        Camera.Camera3D.Resource camera,
        List<LightData> lightDataList,
        Color backgroundColor,
        Color ambientColor,
        float ambientIntensity)
    {
        if (Framebuffer == null || RenderPass == null || _pipeline == null || _descriptorSet == null)
            return;

        // Update lighting uniform buffer
        var ubo = new LightingPassUBO
        {
            CameraPosition = camera.Position,
            AmbientColor = new Vector3(
                ambientColor.R / 255f * ambientIntensity,
                ambientColor.G / 255f * ambientIntensity,
                ambientColor.B / 255f * ambientIntensity)
        };
        _cameraUniformBuffer!.Upload(new ReadOnlySpan<LightingPassUBO>(ref ubo));

        // Update lights buffer
        var lightsData = new LightsBufferData { LightCount = lightDataList.Count };
        for (int i = 0; i < Math.Min(lightDataList.Count, RenderContext3D.MaxLights); i++)
        {
            lightsData.SetLight(i, lightDataList[i]);
        }
        _lightsBuffer!.Upload(new ReadOnlySpan<LightsBufferData>(ref lightsData));

        // Begin lighting pass
        Span<Color> clearColors = [backgroundColor];
        BeginPass(clearColors, 1.0f);

        // Bind lighting pipeline and descriptor set
        RenderPass.BindPipeline(_pipeline);
        RenderPass.BindDescriptorSet(_pipeline, _descriptorSet);

        // Draw fullscreen triangle
        RenderPass.Draw(3);

        EndPass();
    }

    protected override void OnDispose()
    {
        _descriptorSet?.Dispose();
        _pipeline?.Dispose();
        _lightsBuffer?.Dispose();
        _cameraUniformBuffer?.Dispose();
        _gBufferSampler?.Dispose();
        Framebuffer?.Dispose();
        RenderPass?.Dispose();
        OutputTexture?.Dispose();
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

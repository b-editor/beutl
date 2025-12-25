using System;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Lighting;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics3D.Materials;

/// <summary>
/// A Physically Based Rendering (PBR) material using the metallic-roughness workflow.
/// </summary>
public sealed partial class PBRMaterial : Material3D
{
    public PBRMaterial()
    {
        ScanProperties<PBRMaterial>();
    }

    /// <summary>
    /// Gets the base color (albedo) of the material.
    /// </summary>
    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public IProperty<Color> Albedo { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the metallic factor (0 = dielectric, 1 = metal).
    /// </summary>
    [Range(0f, 1f)]
    public IProperty<float> Metallic { get; } = Property.CreateAnimatable(0f);

    /// <summary>
    /// Gets the roughness factor (0 = smooth, 1 = rough).
    /// </summary>
    [Range(0f, 1f)]
    public IProperty<float> Roughness { get; } = Property.CreateAnimatable(0.5f);

    /// <summary>
    /// Gets the ambient occlusion factor.
    /// </summary>
    [Range(0f, 1f)]
    public IProperty<float> AmbientOcclusion { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the emissive color.
    /// </summary>
    public IProperty<Color> Emissive { get; } = Property.CreateAnimatable(Colors.Black);

    /// <summary>
    /// Gets the emissive intensity.
    /// </summary>
    [Range(0f, float.MaxValue)]
    public IProperty<float> EmissiveIntensity { get; } = Property.CreateAnimatable(1f);

    public partial class Resource
    {
        private IPipeline3D? _pipeline;
        private IDescriptorSet? _descriptorSet;
        private IBuffer? _uniformBuffer;
        private IBuffer? _lightsBuffer;

        internal override IPipeline3D? Pipeline => _pipeline;

        public override void EnsurePipeline(RenderContext3D context)
        {
            if (IsPipelineInitialized)
                return;

            var graphicsContext = context.GraphicsContext;
            var shaderCompiler = context.ShaderCompiler;

            // Create uniform buffer for material properties
            _uniformBuffer = graphicsContext.CreateBuffer(
                (ulong)Marshal.SizeOf<PBRMaterialUBO>(),
                BufferUsage.UniformBuffer,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

            // Create lights buffer (supports up to MaxLights)
            _lightsBuffer = graphicsContext.CreateBuffer(
                (ulong)(Marshal.SizeOf<LightData>() * RenderContext3D.MaxLights + 16), // +16 for light count + padding
                BufferUsage.UniformBuffer,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

            // Compile shaders
            var vertexSpirv = shaderCompiler.CompileToSpirv(VertexShaderSource, ShaderStage.Vertex);
            var fragmentSpirv = shaderCompiler.CompileToSpirv(FragmentShaderSource, ShaderStage.Fragment);

            // Create descriptor bindings
            var descriptorBindings = new DescriptorBinding[]
            {
                new(0, DescriptorType.UniformBuffer, 1, ShaderStage.Vertex | ShaderStage.Fragment),
                new(1, DescriptorType.UniformBuffer, 1, ShaderStage.Fragment)
            };

            // Create pipeline
            _pipeline = graphicsContext.CreatePipeline3D(
                context.RenderPass,
                vertexSpirv,
                fragmentSpirv,
                descriptorBindings);

            // Create descriptor set
            var poolSizes = new DescriptorPoolSize[]
            {
                new(DescriptorType.UniformBuffer, 2)
            };

            _descriptorSet = graphicsContext.CreateDescriptorSet(_pipeline, poolSizes);
            _descriptorSet.UpdateBuffer(0, _uniformBuffer);
            _descriptorSet.UpdateBuffer(1, _lightsBuffer);

            IsPipelineInitialized = true;
        }

        public override void Bind(RenderContext3D context, Object3D.Resource obj)
        {
            if (_pipeline == null || _descriptorSet == null || _uniformBuffer == null || _lightsBuffer == null)
                return;

            var renderPass = context.RenderPass;

            // Update material uniform buffer
            var ubo = new PBRMaterialUBO
            {
                Model = obj.GetWorldMatrix(),
                View = context.ViewMatrix,
                Projection = context.ProjectionMatrix,
                ViewPosition = context.CameraPosition,
                Albedo = new Vector4(
                    Albedo.R / 255f,
                    Albedo.G / 255f,
                    Albedo.B / 255f,
                    Albedo.A / 255f),
                Emissive = new Vector3(
                    Emissive.R / 255f * EmissiveIntensity,
                    Emissive.G / 255f * EmissiveIntensity,
                    Emissive.B / 255f * EmissiveIntensity),
                Metallic = Metallic,
                Roughness = Roughness,
                AmbientOcclusion = AmbientOcclusion,
                AmbientColor = context.AmbientColor
            };

            _uniformBuffer.Upload(new ReadOnlySpan<PBRMaterialUBO>(ref ubo));

            // Update lights buffer
            var lights = context.Lights;
            int lightCount = Math.Min(lights.Count, RenderContext3D.MaxLights);

            // Create lights data with count header
            var lightsData = new LightsBufferData
            {
                LightCount = lightCount
            };

            for (int i = 0; i < lightCount; i++)
            {
                lightsData.SetLight(i, lights[i]);
            }

            _lightsBuffer.Upload(new ReadOnlySpan<LightsBufferData>(ref lightsData));

            // Bind pipeline and descriptor set
            renderPass.BindPipeline(_pipeline);
            renderPass.BindDescriptorSet(_pipeline, _descriptorSet);
        }

        partial void PostDispose(bool disposing)
        {
            _descriptorSet?.Dispose();
            _descriptorSet = null;
            _lightsBuffer?.Dispose();
            _lightsBuffer = null;
            _uniformBuffer?.Dispose();
            _uniformBuffer = null;
            _pipeline?.Dispose();
            _pipeline = null;
        }

        /// <summary>
        /// Uniform buffer object for PBR material.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PBRMaterialUBO
        {
            public Matrix4x4 Model;
            public Matrix4x4 View;
            public Matrix4x4 Projection;
            public Vector3 ViewPosition;
            private float _pad1;
            public Vector4 Albedo;
            public Vector3 Emissive;
            public float Metallic;
            public float Roughness;
            public float AmbientOcclusion;
            private Vector2 _pad2;
            public Vector3 AmbientColor;
            private float _pad3;
        }

        /// <summary>
        /// Buffer containing all lights data.
        /// </summary>
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

        private static string VertexShaderSource => """
            #version 450

            layout(location = 0) in vec3 inPosition;
            layout(location = 1) in vec3 inNormal;
            layout(location = 2) in vec2 inTexCoord;

            layout(binding = 0) uniform MaterialUBO {
                mat4 model;
                mat4 view;
                mat4 projection;
                vec3 viewPosition;
                float _pad1;
                vec4 albedo;
                vec3 emissive;
                float metallic;
                float roughness;
                float ambientOcclusion;
                vec2 _pad2;
                vec3 ambientColor;
                float _pad3;
            } material;

            layout(location = 0) out vec3 fragWorldPos;
            layout(location = 1) out vec3 fragNormal;
            layout(location = 2) out vec2 fragTexCoord;

            void main() {
                vec4 worldPos = material.model * vec4(inPosition, 1.0);
                gl_Position = material.projection * material.view * worldPos;

                fragWorldPos = worldPos.xyz;
                fragNormal = mat3(transpose(inverse(material.model))) * inNormal;
                fragTexCoord = inTexCoord;
            }
            """;

        private static string FragmentShaderSource => """
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

            layout(location = 0) in vec3 fragWorldPos;
            layout(location = 1) in vec3 fragNormal;
            layout(location = 2) in vec2 fragTexCoord;

            layout(binding = 0) uniform MaterialUBO {
                mat4 model;
                mat4 view;
                mat4 projection;
                vec3 viewPosition;
                float _pad1;
                vec4 albedo;
                vec3 emissive;
                float metallic;
                float roughness;
                float ambientOcclusion;
                vec2 _pad2;
                vec3 ambientColor;
                float _pad3;
            } material;

            layout(binding = 1) uniform LightsUBO {
                int lightCount;
                int _pad1;
                int _pad2;
                int _pad3;
                Light lights[MAX_LIGHTS];
            } lighting;

            layout(location = 0) out vec4 outColor;

            // Normal Distribution Function (GGX/Trowbridge-Reitz)
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

            // Geometry Function (Schlick-GGX)
            float GeometrySchlickGGX(float NdotV, float roughness) {
                float r = (roughness + 1.0);
                float k = (r * r) / 8.0;

                float num = NdotV;
                float denom = NdotV * (1.0 - k) + k;

                return num / denom;
            }

            // Smith's method for geometry
            float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness) {
                float NdotV = max(dot(N, V), 0.0);
                float NdotL = max(dot(N, L), 0.0);
                float ggx2 = GeometrySchlickGGX(NdotV, roughness);
                float ggx1 = GeometrySchlickGGX(NdotL, roughness);

                return ggx1 * ggx2;
            }

            // Fresnel-Schlick approximation
            vec3 fresnelSchlick(float cosTheta, vec3 F0) {
                return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
            }

            // Calculate attenuation for point/spot lights
            float calculateAttenuation(float distance, float constant, float linear, float quadratic) {
                return 1.0 / (constant + linear * distance + quadratic * distance * distance);
            }

            // Calculate light contribution for a single light
            vec3 calculateLight(Light light, vec3 N, vec3 V, vec3 F0, vec3 albedo, float metallic, float roughness) {
                vec3 L;
                float attenuation = 1.0;
                float spotEffect = 1.0;

                if (light.type == LIGHT_DIRECTIONAL) {
                    L = normalize(-light.positionOrDirection);
                } else {
                    vec3 lightToFrag = light.positionOrDirection - fragWorldPos;
                    float distance = length(lightToFrag);
                    
                    if (distance > light.range) {
                        return vec3(0.0);
                    }
                    
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

                // Cook-Torrance BRDF
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
                vec3 N = normalize(fragNormal);
                vec3 V = normalize(material.viewPosition - fragWorldPos);

                vec3 albedo = material.albedo.rgb;
                float metallic = material.metallic;
                float roughness = max(material.roughness, 0.04); // Prevent division issues
                float ao = material.ambientOcclusion;

                // Calculate reflectance at normal incidence
                vec3 F0 = vec3(0.04);
                F0 = mix(F0, albedo, metallic);

                // Accumulate lighting
                vec3 Lo = vec3(0.0);
                for (int i = 0; i < lighting.lightCount && i < MAX_LIGHTS; i++) {
                    Lo += calculateLight(lighting.lights[i], N, V, F0, albedo, metallic, roughness);
                }

                // Ambient lighting
                vec3 ambient = material.ambientColor * albedo * ao;

                // Final color
                vec3 color = ambient + Lo + material.emissive;

                // Tone mapping (Reinhard)
                color = color / (color + vec3(1.0));

                // Gamma correction
                color = pow(color, vec3(1.0 / 2.2));

                outColor = vec4(color, material.albedo.a);
            }
            """;
    }
}

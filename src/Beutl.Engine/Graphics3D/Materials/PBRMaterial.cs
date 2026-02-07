using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Meshes;
using Beutl.Graphics3D.Textures;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics3D.Materials;

/// <summary>
/// A Physically Based Rendering (PBR) material using the metallic-roughness workflow.
/// </summary>
[Display(Name = nameof(Strings.PBRMaterial), ResourceType = typeof(Strings))]
public sealed partial class PBRMaterial : Material3D
{
    public PBRMaterial()
    {
        ScanProperties<PBRMaterial>();
    }

    /// <summary>
    /// Gets the base color (albedo) of the material.
    /// </summary>
    [Display(Name = nameof(Strings.Albedo), ResourceType = typeof(Strings))]
    public IProperty<Color> Albedo { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the albedo/base color texture map.
    /// </summary>
    [Display(Name = nameof(Strings.AlbedoMap), ResourceType = typeof(Strings))]
    public IProperty<TextureSource?> AlbedoMap { get; } = Property.Create<TextureSource?>(null);

    /// <summary>
    /// Gets the metallic factor (0 = dielectric, 1 = metal).
    /// </summary>
    [Display(Name = nameof(Strings.Metallic), ResourceType = typeof(Strings))]
    [Range(0f, 1f)]
    public IProperty<float> Metallic { get; } = Property.CreateAnimatable(0f);

    /// <summary>
    /// Gets the roughness factor (0 = smooth, 1 = rough).
    /// </summary>
    [Display(Name = nameof(Strings.Roughness), ResourceType = typeof(Strings))]
    [Range(0f, 1f)]
    public IProperty<float> Roughness { get; } = Property.CreateAnimatable(0.5f);

    /// <summary>
    /// Gets the metallic-roughness texture map.
    /// Red channel: unused, Green channel: roughness, Blue channel: metallic.
    /// </summary>
    [Display(Name = nameof(Strings.MetallicRoughnessMap), ResourceType = typeof(Strings))]
    public IProperty<TextureSource?> MetallicRoughnessMap { get; } = Property.Create<TextureSource?>(null);

    /// <summary>
    /// Gets the ambient occlusion factor.
    /// </summary>
    [Display(Name = nameof(Strings.AmbientOcclusion), ResourceType = typeof(Strings))]
    [Range(0f, 1f)]
    public IProperty<float> AmbientOcclusion { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the ambient occlusion texture map (red channel used).
    /// </summary>
    [Display(Name = nameof(Strings.AOMap), ResourceType = typeof(Strings))]
    public IProperty<TextureSource?> AOMap { get; } = Property.Create<TextureSource?>(null);

    /// <summary>
    /// Gets the emissive color.
    /// </summary>
    [Display(Name = nameof(Strings.Emissive), ResourceType = typeof(Strings))]
    public IProperty<Color> Emissive { get; } = Property.CreateAnimatable(Colors.Black);

    /// <summary>
    /// Gets the emissive intensity.
    /// </summary>
    [Display(Name = nameof(Strings.EmissiveIntensity), ResourceType = typeof(Strings))]
    [Range(0f, float.MaxValue)]
    public IProperty<float> EmissiveIntensity { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the emissive texture map.
    /// </summary>
    [Display(Name = nameof(Strings.EmissiveMap), ResourceType = typeof(Strings))]
    public IProperty<TextureSource?> EmissiveMap { get; } = Property.Create<TextureSource?>(null);

    /// <summary>
    /// Gets the normal map for surface detail.
    /// </summary>
    [Display(Name = nameof(Strings.NormalMap), ResourceType = typeof(Strings))]
    public IProperty<TextureSource?> NormalMap { get; } = Property.Create<TextureSource?>(null);

    /// <summary>
    /// Gets the normal map strength (0 = flat, 1 = full effect).
    /// </summary>
    [Display(Name = nameof(Strings.NormalMapStrength), ResourceType = typeof(Strings))]
    [Range(0f, 2f)]
    public IProperty<float> NormalMapStrength { get; } = Property.CreateAnimatable(1f);

    public partial class Resource
    {
        private IPipeline3D? _pipeline;
        private IDescriptorSet? _descriptorSet;
        private IBuffer? _uniformBuffer;
        private ISampler? _sampler;

        // Default textures (1x1 pixel)
        private ITexture2D? _defaultWhiteTexture;
        private ITexture2D? _defaultNormalTexture;
        private ITexture2D? _defaultBlackTexture;

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

            // Create sampler for textures
            _sampler = graphicsContext.CreateSampler(
                SamplerFilter.Linear,
                SamplerFilter.Linear,
                SamplerAddressMode.Repeat,
                SamplerAddressMode.Repeat);

            // Create default textures
            CreateDefaultTextures(graphicsContext);

            // Compile shaders for G-Buffer output
            var vertexSpirv = shaderCompiler.CompileToSpirv(VertexShaderSource, ShaderStage.Vertex);
            var fragmentSpirv = shaderCompiler.CompileToSpirv(FragmentShaderSource, ShaderStage.Fragment);

            // Create descriptor bindings (UBO + 5 texture samplers)
            var descriptorBindings = new DescriptorBinding[]
            {
                new(0, DescriptorType.UniformBuffer, 1, ShaderStage.Vertex | ShaderStage.Fragment),
                new(1, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment), // albedoMap
                new(2, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment), // normalMap
                new(3, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment), // metallicRoughnessMap
                new(4, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment), // emissiveMap
                new(5, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment), // aoMap
            };

            // Create pipeline with vertex input for Vertex3D
            _pipeline = graphicsContext.CreatePipeline3D(
                context.RenderPass,
                vertexSpirv,
                fragmentSpirv,
                descriptorBindings,
                Vertex3D.GetVertexInputDescription());

            // Create descriptor set
            var poolSizes = new DescriptorPoolSize[]
            {
                new(DescriptorType.UniformBuffer, 1),
                new(DescriptorType.CombinedImageSampler, 5)
            };

            _descriptorSet = graphicsContext.CreateDescriptorSet(_pipeline, poolSizes);
            _descriptorSet.UpdateBuffer(0, _uniformBuffer);

            // Bind default textures initially
            _descriptorSet.UpdateTexture(1, _defaultWhiteTexture!, _sampler);
            _descriptorSet.UpdateTexture(2, _defaultNormalTexture!, _sampler);
            _descriptorSet.UpdateTexture(3, _defaultWhiteTexture!, _sampler);
            _descriptorSet.UpdateTexture(4, _defaultBlackTexture!, _sampler);
            _descriptorSet.UpdateTexture(5, _defaultWhiteTexture!, _sampler);

            IsPipelineInitialized = true;
        }

        private void CreateDefaultTextures(IGraphicsContext graphicsContext)
        {
            // White texture (1x1, RGBA = 255,255,255,255)
            _defaultWhiteTexture = graphicsContext.CreateTexture2D(1, 1, TextureFormat.BGRA8Unorm);
            _defaultWhiteTexture.Upload([255, 255, 255, 255]);

            // Normal map default (1x1, RGB = 128,128,255 = flat normal pointing up in tangent space)
            _defaultNormalTexture = graphicsContext.CreateTexture2D(1, 1, TextureFormat.BGRA8Unorm);
            _defaultNormalTexture.Upload([255, 128, 128, 255]); // BGRA: B=255, G=128, R=128

            // Black texture (1x1, RGBA = 0,0,0,255)
            _defaultBlackTexture = graphicsContext.CreateTexture2D(1, 1, TextureFormat.BGRA8Unorm);
            _defaultBlackTexture.Upload([0, 0, 0, 255]);
        }

        public override void Bind(RenderContext3D context, Object3D.Resource obj, Matrix4x4 worldMatrix)
        {
            if (_pipeline == null || _descriptorSet == null || _uniformBuffer == null || _sampler == null)
                return;

            var renderPass = context.RenderPass;
            var graphicsContext = context.GraphicsContext;

            // Determine which textures are available
            int textureFlags = 0;
            ITexture2D? albedoTex = AlbedoMap?.GetTexture(graphicsContext);
            ITexture2D? normalTex = NormalMap?.GetTexture(graphicsContext);
            ITexture2D? metallicRoughnessTex = MetallicRoughnessMap?.GetTexture(graphicsContext);
            ITexture2D? emissiveTex = EmissiveMap?.GetTexture(graphicsContext);
            ITexture2D? aoTex = AOMap?.GetTexture(graphicsContext);

            if (albedoTex != null) textureFlags |= 1;
            if (normalTex != null) textureFlags |= 2;
            if (metallicRoughnessTex != null) textureFlags |= 4;
            if (emissiveTex != null) textureFlags |= 8;
            if (aoTex != null) textureFlags |= 16;

            // Update texture bindings
            _descriptorSet.UpdateTexture(1, albedoTex ?? _defaultWhiteTexture!, _sampler);
            _descriptorSet.UpdateTexture(2, normalTex ?? _defaultNormalTexture!, _sampler);
            _descriptorSet.UpdateTexture(3, metallicRoughnessTex ?? _defaultWhiteTexture!, _sampler);
            _descriptorSet.UpdateTexture(4, emissiveTex ?? _defaultBlackTexture!, _sampler);
            _descriptorSet.UpdateTexture(5, aoTex ?? _defaultWhiteTexture!, _sampler);

            // Update material uniform buffer
            var ubo = new PBRMaterialUBO
            {
                Model = worldMatrix,
                View = context.ViewMatrix,
                Projection = context.ProjectionMatrix,
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
                NormalMapStrength = NormalMapStrength,
                TextureFlags = textureFlags
            };

            _uniformBuffer.Upload(new ReadOnlySpan<PBRMaterialUBO>(ref ubo));

            // Bind pipeline and descriptor set
            renderPass.BindPipeline(_pipeline);
            renderPass.BindDescriptorSet(_pipeline, _descriptorSet);
        }

        partial void PostDispose(bool disposing)
        {
            _descriptorSet?.Dispose();
            _descriptorSet = null;
            _uniformBuffer?.Dispose();
            _uniformBuffer = null;
            _sampler?.Dispose();
            _sampler = null;

            _defaultWhiteTexture?.Dispose();
            _defaultNormalTexture?.Dispose();
            _defaultBlackTexture?.Dispose();
            _defaultWhiteTexture = null;
            _defaultNormalTexture = null;
            _defaultBlackTexture = null;

            _pipeline?.Dispose();
            _pipeline = null;
        }

        /// <summary>
        /// Uniform buffer object for PBR material (geometry pass).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PBRMaterialUBO
        {
            public Matrix4x4 Model;
            public Matrix4x4 View;
            public Matrix4x4 Projection;
            public Vector4 Albedo;
            public Vector3 Emissive;
            public float Metallic;
            public float Roughness;
            public float AmbientOcclusion;
            public float NormalMapStrength;
            public int TextureFlags; // bit flags: 1=albedo, 2=normal, 4=metallicRoughness, 8=emissive, 16=ao
        }

        /// <summary>
        /// Vertex shader for G-Buffer geometry pass with tangent support.
        /// </summary>
        private static string VertexShaderSource => """
            #version 450

            layout(location = 0) in vec3 inPosition;
            layout(location = 1) in vec3 inNormal;
            layout(location = 2) in vec2 inTexCoord;
            layout(location = 3) in vec4 inTangent;

            layout(binding = 0) uniform MaterialUBO {
                mat4 model;
                mat4 view;
                mat4 projection;
                vec4 albedo;
                vec3 emissive;
                float metallic;
                float roughness;
                float ambientOcclusion;
                float normalMapStrength;
                int textureFlags;
            } material;

            layout(location = 0) out vec3 fragWorldPos;
            layout(location = 1) out vec3 fragNormal;
            layout(location = 2) out vec2 fragTexCoord;
            layout(location = 3) out mat3 fragTBN;

            void main() {
                vec4 worldPos = material.model * vec4(inPosition, 1.0);
                gl_Position = material.projection * material.view * worldPos;

                fragWorldPos = worldPos.xyz;

                mat3 normalMatrix = mat3(transpose(inverse(material.model)));
                vec3 N = normalize(normalMatrix * inNormal);
                vec3 T = normalize(normalMatrix * inTangent.xyz);
                // Re-orthogonalize T with respect to N
                T = normalize(T - dot(T, N) * N);
                vec3 B = cross(N, T) * inTangent.w;

                fragNormal = N;
                fragTexCoord = inTexCoord;
                fragTBN = mat3(T, B, N);
            }
            """;

        /// <summary>
        /// Fragment shader for G-Buffer output with texture support.
        /// Outputs: Position, Normal+Metallic, Albedo+Roughness, Emission+AO
        /// </summary>
        private static string FragmentShaderSource => """
            #version 450

            layout(location = 0) in vec3 fragWorldPos;
            layout(location = 1) in vec3 fragNormal;
            layout(location = 2) in vec2 fragTexCoord;
            layout(location = 3) in mat3 fragTBN;

            layout(binding = 0) uniform MaterialUBO {
                mat4 model;
                mat4 view;
                mat4 projection;
                vec4 albedo;
                vec3 emissive;
                float metallic;
                float roughness;
                float ambientOcclusion;
                float normalMapStrength;
                int textureFlags;
            } material;

            layout(binding = 1) uniform sampler2D albedoMap;
            layout(binding = 2) uniform sampler2D normalMap;
            layout(binding = 3) uniform sampler2D metallicRoughnessMap;
            layout(binding = 4) uniform sampler2D emissiveMap;
            layout(binding = 5) uniform sampler2D aoMap;

            // G-Buffer outputs
            layout(location = 0) out vec4 outPosition;       // World position (RGB) + Valid flag (A)
            layout(location = 1) out vec4 outNormalMetallic; // Normal (RGB encoded) + Metallic (A)
            layout(location = 2) out vec4 outAlbedoRoughness; // Albedo (RGB) + Roughness (A)
            layout(location = 3) out vec4 outEmissionAO;     // Emission (RGB) + AO (A)

            void main() {
                // Sample textures
                vec4 albedoSample = texture(albedoMap, fragTexCoord);
                vec4 metallicRoughnessSample = texture(metallicRoughnessMap, fragTexCoord);
                vec4 emissiveSample = texture(emissiveMap, fragTexCoord);
                float aoSample = texture(aoMap, fragTexCoord).r;

                // Calculate final values
                vec4 finalAlbedo = material.albedo;
                if ((material.textureFlags & 1) != 0) {
                    finalAlbedo *= albedoSample;
                }

                float finalMetallic = material.metallic;
                float finalRoughness = material.roughness;
                if ((material.textureFlags & 4) != 0) {
                    finalMetallic *= metallicRoughnessSample.b;
                    finalRoughness *= metallicRoughnessSample.g;
                }

                vec3 finalEmissive = material.emissive;
                if ((material.textureFlags & 8) != 0) {
                    finalEmissive *= emissiveSample.rgb;
                }

                float finalAO = material.ambientOcclusion;
                if ((material.textureFlags & 16) != 0) {
                    finalAO *= aoSample;
                }

                // Normal mapping
                vec3 N = normalize(fragNormal);
                if ((material.textureFlags & 2) != 0) {
                    vec3 normalSample = texture(normalMap, fragTexCoord).rgb;
                    normalSample = normalSample * 2.0 - 1.0;
                    normalSample.xy *= material.normalMapStrength;
                    N = normalize(fragTBN * normalSample);
                }

                // Output world position with valid flag
                outPosition = vec4(fragWorldPos, 1.0);

                // Output normal (encoded to [0,1] range) with metallic
                outNormalMetallic = vec4(N * 0.5 + 0.5, finalMetallic);

                // Output albedo with roughness
                outAlbedoRoughness = vec4(finalAlbedo.rgb, finalRoughness);

                // Output emission with ambient occlusion
                outEmissionAO = vec4(finalEmissive, finalAO);
            }
            """;
    }
}

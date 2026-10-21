using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Textures;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics3D.Materials;

/// <summary>
/// A Physically Based Rendering (PBR) material using the metallic-roughness workflow.
/// </summary>
[Display(Name = nameof(GraphicsStrings.PBRMaterial), ResourceType = typeof(GraphicsStrings))]
public sealed partial class PBRMaterial : Material3D
{
    public PBRMaterial()
    {
        ScanProperties<PBRMaterial>();
    }

    /// <summary>
    /// Gets the base color (albedo) of the material.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PBRMaterial_Albedo), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Albedo { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the albedo/base color texture map.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PBRMaterial_AlbedoMap), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> AlbedoMap { get; } = Property.CreateWithExpression<TextureSource?>(null);

    /// <summary>
    /// Gets the metallic factor (0 = dielectric, 1 = metal).
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PBRMaterial_Metallic), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 1f), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Metallic { get; } = Property.CreateAnimatable(0f);

    /// <summary>
    /// Gets the roughness factor (0 = smooth, 1 = rough).
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PBRMaterial_Roughness), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 1f), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Roughness { get; } = Property.CreateAnimatable(0.5f);

    /// <summary>
    /// Gets the metallic-roughness texture map.
    /// Red channel: unused, Green channel: roughness, Blue channel: metallic.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PBRMaterial_MetallicRoughnessMap), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> MetallicRoughnessMap { get; } = Property.CreateWithExpression<TextureSource?>(null);

    /// <summary>
    /// Gets the ambient occlusion factor.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PBRMaterial_AmbientOcclusion), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 1f), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> AmbientOcclusion { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the ambient occlusion texture map (red channel used).
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PBRMaterial_AOMap), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> AOMap { get; } = Property.CreateWithExpression<TextureSource?>(null);

    /// <summary>
    /// Gets the emissive color.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PBRMaterial_Emissive), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Emissive { get; } = Property.CreateAnimatable(Colors.Black);

    /// <summary>
    /// Gets the emissive intensity.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PBRMaterial_EmissiveIntensity), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, float.MaxValue), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> EmissiveIntensity { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the emissive texture map.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PBRMaterial_EmissiveMap), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> EmissiveMap { get; } = Property.CreateWithExpression<TextureSource?>(null);

    /// <summary>
    /// Gets the normal map for surface detail.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PBRMaterial_NormalMap), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> NormalMap { get; } = Property.CreateWithExpression<TextureSource?>(null);

    /// <summary>
    /// Gets the normal map strength (0 = flat, 1 = full effect).
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PBRMaterial_NormalMapStrength), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 2f), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> NormalMapStrength { get; } = Property.CreateAnimatable(1f);

    public partial class Resource
    {
        // The order of the fallback textures EnsurePipeline builds.
        private const int WhiteFallback = 0;
        private const int FlatNormalFallback = 1;
        private const int BlackFallback = 2;

        private readonly MaterialPipelineState _pipelineState = new();

        protected internal override IPipeline3D? Pipeline => _pipelineState.Pipeline;

        // Test seam: only the draw-binding reuse tests read this.
        internal MaterialDrawBindingPool? DrawBindings => _pipelineState.DrawBindings;

        protected internal override IEnumerable<TextureSource.Resource> EnumerateTextureSources()
        {
            if (AlbedoMap is not null)
                yield return AlbedoMap;
            if (NormalMap is not null)
                yield return NormalMap;
            if (MetallicRoughnessMap is not null)
                yield return MetallicRoughnessMap;
            if (EmissiveMap is not null)
                yield return EmissiveMap;
            if (AOMap is not null)
                yield return AOMap;
        }

        public override void EnsurePipeline(RenderContext3D context)
        {
            if (IsPipelineInitialized && _pipelineState.IsBuiltFor(context.RenderPass))
                return;

            PostDispose(true);
            IsPipelineInitialized = false;

            // UBO + 5 texture samplers: albedoMap, normalMap, metallicRoughnessMap, emissiveMap, aoMap
            _pipelineState.Build<PBRMaterialUBO>(
                context,
                VertexShaderSource,
                FragmentShaderSource,
                textureCount: 5,
                options: null,
                [
                    [255, 255, 255, 255],
                    // BGRA (255,128,128) is RGB (128,128,255): the flat tangent-space normal pointing up.
                    [255, 128, 128, 255],
                    [0, 0, 0, 255],
                ]);

            IsPipelineInitialized = true;
        }

        public override void Bind(RenderContext3D context, Object3D.Resource obj, Matrix4x4 worldMatrix)
        {
            if (!_pipelineState.IsBuilt)
                return;

            var renderPass = context.RenderPass;
            var graphicsContext = context.GraphicsContext;

            // Determine which textures are available. Normal, metallic-roughness and AO maps hold raw values, not colors.
            int textureFlags = 0;
            ITexture2D? albedoTex = AlbedoMap?.GetTexture(graphicsContext, context.SurfaceDensity);
            ITexture2D? normalTex = NormalMap?.GetTexture(graphicsContext, context.SurfaceDensity, TextureContentKind.Data);
            ITexture2D? metallicRoughnessTex = MetallicRoughnessMap?.GetTexture(graphicsContext, context.SurfaceDensity, TextureContentKind.Data);
            ITexture2D? emissiveTex = EmissiveMap?.GetTexture(graphicsContext, context.SurfaceDensity);
            ITexture2D? aoTex = AOMap?.GetTexture(graphicsContext, context.SurfaceDensity, TextureContentKind.Data);

            if (albedoTex != null) textureFlags |= 1;
            if (normalTex != null) textureFlags |= 2;
            if (metallicRoughnessTex != null) textureFlags |= 4;
            if (emissiveTex != null) textureFlags |= 8;
            if (aoTex != null) textureFlags |= 16;

            // Recorded draws must retain immutable bindings until the backend completes them.
            MaterialDrawBindings bindings = _pipelineState.DrawBindings.Acquire();

            _pipelineState.UpdateTexture(bindings, 1, albedoTex, WhiteFallback);
            _pipelineState.UpdateTexture(bindings, 2, normalTex, FlatNormalFallback);
            _pipelineState.UpdateTexture(bindings, 3, metallicRoughnessTex, WhiteFallback);
            _pipelineState.UpdateTexture(bindings, 4, emissiveTex, BlackFallback);
            _pipelineState.UpdateTexture(bindings, 5, aoTex, WhiteFallback);

            var ubo = new PBRMaterialUBO
            {
                Model = worldMatrix,
                View = context.ViewMatrix,
                Projection = context.ProjectionMatrix,
                Albedo = Albedo.ToLinearPremultiplied(),
                Emissive = Emissive.ToLinearPremultiplied().AsVector3() * EmissiveIntensity,
                Metallic = Metallic,
                Roughness = Roughness,
                AmbientOcclusion = AmbientOcclusion,
                NormalMapStrength = NormalMapStrength,
                TextureFlags = textureFlags | (obj.ReceiveShadows ? 0 : 32)
            };

            _pipelineState.DrawBindings.UploadAndBind(bindings, in ubo, renderPass, _pipelineState.Pipeline);
        }

        partial void PostDispose(bool disposing)
        {
            IsPipelineInitialized = false;
            _pipelineState.Dispose();
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
            public int TextureFlags; // bit flags: 1=albedo, 2=normal, 4=metallicRoughness, 8=emissive, 16=ao, 32=no shadows
        }

        // Both stages bind this block at descriptor binding 0, so they must declare the same fields in
        // the same order as PBRMaterialUBO.
        private const string MaterialUboSource = """
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
            """;

        /// <summary>
        /// Vertex shader for G-Buffer geometry pass with tangent support.
        /// </summary>
        private static string VertexShaderSource => $$"""
            #version 450

            layout(location = 0) in vec3 inPosition;
            layout(location = 1) in vec3 inNormal;
            layout(location = 2) in vec2 inTexCoord;
            layout(location = 3) in vec4 inTangent;

            {{MaterialUboSource}}

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
        private static string FragmentShaderSource => $$"""
            #version 450

            layout(location = 0) in vec3 fragWorldPos;
            layout(location = 1) in vec3 fragNormal;
            layout(location = 2) in vec2 fragTexCoord;
            layout(location = 3) in mat3 fragTBN;

            {{MaterialUboSource}}

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
                outPosition = vec4(fragWorldPos, (material.textureFlags & 32) == 0 ? 1.0 : 2.0);

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

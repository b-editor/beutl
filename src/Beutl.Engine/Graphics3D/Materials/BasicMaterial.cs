using System;
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
/// A basic material with diffuse color using Blinn-Phong lighting.
/// </summary>
[Display(Name = nameof(GraphicsStrings.BasicMaterial), ResourceType = typeof(GraphicsStrings))]
public sealed partial class BasicMaterial : Material3D
{
    public BasicMaterial()
    {
        ScanProperties<BasicMaterial>();
    }

    /// <summary>
    /// Gets the diffuse color of the material.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.BasicMaterial_DiffuseColor), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> DiffuseColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the diffuse texture map.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.BasicMaterial_DiffuseMap), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> DiffuseMap { get; } = Property.CreateExpression<TextureSource?>(null);

    /// <summary>
    /// Gets the ambient color contribution.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.BasicMaterial_AmbientColor), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> AmbientColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the specular color for highlights.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.BasicMaterial_SpecularColor), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> SpecularColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the shininess factor for specular highlights.
    /// Higher values create sharper highlights. Converted to roughness for PBR.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.BasicMaterial_Shininess), ResourceType = typeof(GraphicsStrings))]
    [Range(1f, 256f)]
    public IExpressionProperty<float> Shininess { get; } = Property.CreateAnimatable(32f);

    public partial class Resource
    {
        private readonly MaterialPipelineState _pipelineState = new();

        protected internal override IPipeline3D? Pipeline => _pipelineState.Pipeline;

        // Test seam: only the draw-binding reuse tests read this.
        internal MaterialDrawBindingPool? DrawBindings => _pipelineState.DrawBindings;

        protected internal override IEnumerable<TextureSource.Resource> EnumerateTextureSources()
        {
            if (DiffuseMap is not null)
                yield return DiffuseMap;
        }

        public override void EnsurePipeline(RenderContext3D context)
        {
            if (IsPipelineInitialized && _pipelineState.IsBuiltFor(context.RenderPass))
                return;

            PostDispose(true);
            IsPipelineInitialized = false;

            // UBO + 1 texture sampler: diffuseMap, with a white fallback
            _pipelineState.Build<BasicMaterialUBO>(
                context,
                VertexShaderSource,
                FragmentShaderSource,
                textureCount: 1,
                options: null,
                [[255, 255, 255, 255]]);

            IsPipelineInitialized = true;
        }

        public override void Bind(RenderContext3D context, Object3D.Resource obj, Matrix4x4 worldMatrix)
        {
            if (!_pipelineState.IsBuilt)
                return;

            var renderPass = context.RenderPass;
            var graphicsContext = context.GraphicsContext;

            ITexture2D? diffuseTex = DiffuseMap?.GetTexture(graphicsContext, context.SurfaceDensity);
            int hasTexture = diffuseTex != null ? 1 : 0;

            // Recorded draws must retain immutable bindings until the backend completes them.
            MaterialDrawBindings bindings = _pipelineState.DrawBindings.Acquire();

            _pipelineState.UpdateTexture(bindings, 1, diffuseTex);

            // Convert shininess to roughness (inverse relationship)
            // Shininess 1 -> Roughness 1.0, Shininess 256 -> Roughness ~0.04
            float roughness = 1.0f - MathF.Sqrt(Shininess / 256f);
            roughness = Math.Clamp(roughness, 0.04f, 1.0f);

            var ubo = new BasicMaterialUBO
            {
                Model = worldMatrix,
                View = context.ViewMatrix,
                Projection = context.ProjectionMatrix,
                Albedo = DiffuseColor.ToLinearPremultiplied(),
                Roughness = roughness,
                HasTexture = hasTexture | (obj.ReceiveShadows ? 0 : 2)
            };

            _pipelineState.DrawBindings.UploadAndBind(bindings, in ubo, renderPass, _pipelineState.Pipeline);
        }

        partial void PostDispose(bool disposing)
        {
            IsPipelineInitialized = false;
            _pipelineState.Dispose();
        }

        /// <summary>
        /// Uniform buffer object for basic material (geometry pass).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicMaterialUBO
        {
            public Matrix4x4 Model;
            public Matrix4x4 View;
            public Matrix4x4 Projection;
            public Vector4 Albedo;
            public float Roughness;
            public int HasTexture;
            private Vector2 _pad;
        }

        // Both stages bind this block at descriptor binding 0, so they must declare the same fields in
        // the same order as BasicMaterialUBO.
        private const string MaterialUboSource = """
            layout(binding = 0) uniform MaterialUBO {
                mat4 model;
                mat4 view;
                mat4 projection;
                vec4 albedo;
                float roughness;
                int hasTexture;
                vec2 _pad;
            } material;
            """;

        /// <summary>
        /// Vertex shader for G-Buffer geometry pass.
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

            void main() {
                vec4 worldPos = material.model * vec4(inPosition, 1.0);
                gl_Position = material.projection * material.view * worldPos;

                fragWorldPos = worldPos.xyz;
                fragNormal = mat3(transpose(inverse(material.model))) * inNormal;
                fragTexCoord = inTexCoord;
            }
            """;

        /// <summary>
        /// Fragment shader for G-Buffer output.
        /// BasicMaterial is treated as non-metallic with full AO and no emission.
        /// </summary>
        private static string FragmentShaderSource => $$"""
            #version 450

            layout(location = 0) in vec3 fragWorldPos;
            layout(location = 1) in vec3 fragNormal;
            layout(location = 2) in vec2 fragTexCoord;

            {{MaterialUboSource}}

            layout(binding = 1) uniform sampler2D diffuseMap;

            // G-Buffer outputs
            layout(location = 0) out vec4 outPosition;       // World position (RGB) + Valid flag (A)
            layout(location = 1) out vec4 outNormalMetallic; // Normal (RGB encoded) + Metallic (A)
            layout(location = 2) out vec4 outAlbedoRoughness; // Albedo (RGB) + Roughness (A)
            layout(location = 3) out vec4 outEmissionAO;     // Emission (RGB) + AO (A)

            void main() {
                vec3 N = normalize(fragNormal);

                // Sample diffuse texture if available
                vec4 finalAlbedo = material.albedo;
                if ((material.hasTexture & 1) != 0) {
                    vec4 texColor = texture(diffuseMap, fragTexCoord);
                    finalAlbedo *= texColor;
                }

                // Output world position with valid flag
                outPosition = vec4(fragWorldPos, (material.hasTexture & 2) == 0 ? 1.0 : 2.0);

                // Output normal (encoded to [0,1] range) with metallic (0 for basic material)
                outNormalMetallic = vec4(N * 0.5 + 0.5, 0.0);

                // Output albedo with roughness
                outAlbedoRoughness = vec4(finalAlbedo.rgb, material.roughness);

                // Output no emission with full ambient occlusion
                outEmissionAO = vec4(0.0, 0.0, 0.0, 1.0);
            }
            """;
    }
}

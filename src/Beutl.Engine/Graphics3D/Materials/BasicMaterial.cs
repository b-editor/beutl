using System;
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
/// A basic material with diffuse color using Blinn-Phong lighting.
/// </summary>
[Display(Name = nameof(Strings.BasicMaterial), ResourceType = typeof(Strings))]
public sealed partial class BasicMaterial : Material3D
{
    public BasicMaterial()
    {
        ScanProperties<BasicMaterial>();
    }

    /// <summary>
    /// Gets the diffuse color of the material.
    /// </summary>
    [Display(Name = nameof(Strings.DiffuseColor), ResourceType = typeof(Strings))]
    public IProperty<Color> DiffuseColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the diffuse texture map.
    /// </summary>
    [Display(Name = nameof(Strings.DiffuseMap), ResourceType = typeof(Strings))]
    public IProperty<TextureSource?> DiffuseMap { get; } = Property.Create<TextureSource?>(null);

    /// <summary>
    /// Gets the ambient color contribution.
    /// </summary>
    [Display(Name = nameof(Strings.AmbientColor), ResourceType = typeof(Strings))]
    public IProperty<Color> AmbientColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the specular color for highlights.
    /// </summary>
    [Display(Name = nameof(Strings.SpecularColor), ResourceType = typeof(Strings))]
    public IProperty<Color> SpecularColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the shininess factor for specular highlights.
    /// Higher values create sharper highlights. Converted to roughness for PBR.
    /// </summary>
    [Display(Name = nameof(Strings.Shininess), ResourceType = typeof(Strings))]
    [Range(1f, 256f)]
    public IProperty<float> Shininess { get; } = Property.CreateAnimatable(32f);

    public partial class Resource
    {
        private IPipeline3D? _pipeline;
        private IDescriptorSet? _descriptorSet;
        private IBuffer? _uniformBuffer;
        private ISampler? _sampler;

        // Default texture (1x1 pixel)
        private ITexture2D? _defaultWhiteTexture;

        internal override IPipeline3D? Pipeline => _pipeline;

        public override void EnsurePipeline(RenderContext3D context)
        {
            if (IsPipelineInitialized)
                return;

            var graphicsContext = context.GraphicsContext;
            var shaderCompiler = context.ShaderCompiler;

            // Create uniform buffer
            _uniformBuffer = graphicsContext.CreateBuffer(
                (ulong)Marshal.SizeOf<BasicMaterialUBO>(),
                BufferUsage.UniformBuffer,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

            // Create sampler for texture
            _sampler = graphicsContext.CreateSampler(
                SamplerFilter.Linear,
                SamplerFilter.Linear,
                SamplerAddressMode.Repeat,
                SamplerAddressMode.Repeat);

            // Create default white texture
            _defaultWhiteTexture = graphicsContext.CreateTexture2D(1, 1, TextureFormat.BGRA8Unorm);
            _defaultWhiteTexture.Upload([255, 255, 255, 255]);

            // Compile shaders for G-Buffer output
            var vertexSpirv = shaderCompiler.CompileToSpirv(VertexShaderSource, ShaderStage.Vertex);
            var fragmentSpirv = shaderCompiler.CompileToSpirv(FragmentShaderSource, ShaderStage.Fragment);

            // Create descriptor bindings (UBO + 1 texture sampler)
            var descriptorBindings = new DescriptorBinding[]
            {
                new(0, DescriptorType.UniformBuffer, 1, ShaderStage.Vertex | ShaderStage.Fragment),
                new(1, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment), // diffuseMap
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
                new(DescriptorType.CombinedImageSampler, 1)
            };

            _descriptorSet = graphicsContext.CreateDescriptorSet(_pipeline, poolSizes);
            _descriptorSet.UpdateBuffer(0, _uniformBuffer);
            _descriptorSet.UpdateTexture(1, _defaultWhiteTexture, _sampler);

            IsPipelineInitialized = true;
        }

        public override void Bind(RenderContext3D context, Object3D.Resource obj, Matrix4x4 worldMatrix)
        {
            if (_pipeline == null || _descriptorSet == null || _uniformBuffer == null || _sampler == null)
                return;

            var renderPass = context.RenderPass;
            var graphicsContext = context.GraphicsContext;

            // Get texture
            ITexture2D? diffuseTex = DiffuseMap?.GetTexture(graphicsContext);
            int hasTexture = diffuseTex != null ? 1 : 0;

            // Update texture binding
            _descriptorSet.UpdateTexture(1, diffuseTex ?? _defaultWhiteTexture!, _sampler);

            // Convert shininess to roughness (inverse relationship)
            // Shininess 1 -> Roughness 1.0, Shininess 256 -> Roughness ~0.04
            float roughness = 1.0f - MathF.Sqrt(Shininess / 256f);
            roughness = Math.Clamp(roughness, 0.04f, 1.0f);

            // Update uniform buffer
            var ubo = new BasicMaterialUBO
            {
                Model = worldMatrix,
                View = context.ViewMatrix,
                Projection = context.ProjectionMatrix,
                Albedo = new Vector4(
                    DiffuseColor.R / 255f,
                    DiffuseColor.G / 255f,
                    DiffuseColor.B / 255f,
                    DiffuseColor.A / 255f),
                Roughness = roughness,
                HasTexture = hasTexture
            };

            _uniformBuffer.Upload(new ReadOnlySpan<BasicMaterialUBO>(ref ubo));

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
            _defaultWhiteTexture = null;

            _pipeline?.Dispose();
            _pipeline = null;
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

        /// <summary>
        /// Vertex shader for G-Buffer geometry pass.
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
                float roughness;
                int hasTexture;
                vec2 _pad;
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

        /// <summary>
        /// Fragment shader for G-Buffer output.
        /// BasicMaterial is treated as non-metallic with full AO and no emission.
        /// </summary>
        private static string FragmentShaderSource => """
            #version 450

            layout(location = 0) in vec3 fragWorldPos;
            layout(location = 1) in vec3 fragNormal;
            layout(location = 2) in vec2 fragTexCoord;

            layout(binding = 0) uniform MaterialUBO {
                mat4 model;
                mat4 view;
                mat4 projection;
                vec4 albedo;
                float roughness;
                int hasTexture;
                vec2 _pad;
            } material;

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
                if (material.hasTexture != 0) {
                    vec4 texColor = texture(diffuseMap, fragTexCoord);
                    finalAlbedo *= texColor;
                }

                // Output world position with valid flag
                outPosition = vec4(fragWorldPos, 1.0);

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

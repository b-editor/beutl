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

            // Compile shaders for G-Buffer output
            var vertexSpirv = shaderCompiler.CompileToSpirv(VertexShaderSource, ShaderStage.Vertex);
            var fragmentSpirv = shaderCompiler.CompileToSpirv(FragmentShaderSource, ShaderStage.Fragment);

            // Create descriptor bindings (just material UBO)
            var descriptorBindings = new DescriptorBinding[]
            {
                new(0, DescriptorType.UniformBuffer, 1, ShaderStage.Vertex | ShaderStage.Fragment)
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
                new(DescriptorType.UniformBuffer, 1)
            };

            _descriptorSet = graphicsContext.CreateDescriptorSet(_pipeline, poolSizes);
            _descriptorSet.UpdateBuffer(0, _uniformBuffer);

            IsPipelineInitialized = true;
        }

        public override void Bind(RenderContext3D context, Object3D.Resource obj)
        {
            if (_pipeline == null || _descriptorSet == null || _uniformBuffer == null)
                return;

            var renderPass = context.RenderPass;

            // Update material uniform buffer
            var ubo = new PBRMaterialUBO
            {
                Model = obj.GetWorldMatrix(),
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
                AmbientOcclusion = AmbientOcclusion
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

            layout(binding = 0) uniform MaterialUBO {
                mat4 model;
                mat4 view;
                mat4 projection;
                vec4 albedo;
                vec3 emissive;
                float metallic;
                float roughness;
                float ambientOcclusion;
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
        /// Outputs: Position, Normal+Metallic, Albedo+Roughness, Emission+AO
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
                vec3 emissive;
                float metallic;
                float roughness;
                float ambientOcclusion;
                vec2 _pad;
            } material;

            // G-Buffer outputs
            layout(location = 0) out vec4 outPosition;       // World position (RGB) + Valid flag (A)
            layout(location = 1) out vec4 outNormalMetallic; // Normal (RGB encoded) + Metallic (A)
            layout(location = 2) out vec4 outAlbedoRoughness; // Albedo (RGB) + Roughness (A)
            layout(location = 3) out vec4 outEmissionAO;     // Emission (RGB) + AO (A)

            void main() {
                vec3 N = normalize(fragNormal);

                // Output world position with valid flag
                outPosition = vec4(fragWorldPos, 1.0);

                // Output normal (encoded to [0,1] range) with metallic
                outNormalMetallic = vec4(N * 0.5 + 0.5, material.metallic);

                // Output albedo with roughness
                outAlbedoRoughness = vec4(material.albedo.rgb, material.roughness);

                // Output emission with ambient occlusion
                outEmissionAO = vec4(material.emissive, material.ambientOcclusion);
            }
            """;
    }
}

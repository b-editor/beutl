using System;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics3D.Materials;

/// <summary>
/// A basic material with diffuse color using Blinn-Phong lighting.
/// </summary>
public sealed partial class BasicMaterial : Material3D
{
    public BasicMaterial()
    {
        ScanProperties<BasicMaterial>();
    }

    /// <summary>
    /// Gets the diffuse color of the material.
    /// </summary>
    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public IProperty<Color> DiffuseColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the ambient color contribution.
    /// </summary>
    public IProperty<Color> AmbientColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the specular color for highlights.
    /// </summary>
    public IProperty<Color> SpecularColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the shininess factor for specular highlights.
    /// </summary>
    [Range(1f, 256f)]
    public IProperty<float> Shininess { get; } = Property.CreateAnimatable(32f);

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

            // Create uniform buffer
            _uniformBuffer = graphicsContext.CreateBuffer(
                (ulong)Marshal.SizeOf<BasicMaterialUBO>(),
                BufferUsage.UniformBuffer,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

            // Compile shaders
            var vertexSpirv = shaderCompiler.CompileToSpirv(VertexShaderSource, ShaderStage.Vertex);
            var fragmentSpirv = shaderCompiler.CompileToSpirv(FragmentShaderSource, ShaderStage.Fragment);

            // Create descriptor bindings
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

            // Update uniform buffer
            var ubo = new BasicMaterialUBO
            {
                Model = obj.GetWorldMatrix(),
                View = context.ViewMatrix,
                Projection = context.ProjectionMatrix,
                LightDirection = context.LightDirection,
                LightColor = context.LightColor,
                AmbientColor = context.AmbientColor,
                ViewPosition = context.CameraPosition,
                ObjectColor = new Vector4(
                    DiffuseColor.R / 255f,
                    DiffuseColor.G / 255f,
                    DiffuseColor.B / 255f,
                    DiffuseColor.A / 255f),
                Shininess = Shininess
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
            _pipeline?.Dispose();
            _pipeline = null;
        }

        /// <summary>
        /// Uniform buffer object for basic material.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicMaterialUBO
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
            public float Shininess;
            private Vector3 _pad5;
        }

        private static string VertexShaderSource => """
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
                float shininess;
                vec3 _pad5;
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

        private static string FragmentShaderSource => """
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
                float shininess;
                vec3 _pad5;
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

                // Specular (Blinn-Phong)
                vec3 viewDir = normalize(ubo.viewPosition - fragPosition);
                vec3 halfwayDir = normalize(lightDir + viewDir);
                float spec = pow(max(dot(normal, halfwayDir), 0.0), ubo.shininess);
                vec3 specular = spec * ubo.lightColor * 0.5;

                vec3 result = ambient + diffuse + specular;
                outColor = vec4(result, ubo.objectColor.a);
            }
            """;
    }
}

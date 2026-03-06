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
/// A transparent material with fresnel effect and simple lighting for forward rendering.
/// </summary>
[Display(Name = nameof(Strings.TransparentMaterial), ResourceType = typeof(Strings))]
public sealed partial class TransparentMaterial : Material3D
{
    public TransparentMaterial()
    {
        ScanProperties<TransparentMaterial>();
    }

    /// <summary>
    /// Gets the base color of the material.
    /// </summary>
    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public IProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the opacity of the material (0-1).
    /// </summary>
    [Display(Name = nameof(Strings.Opacity), ResourceType = typeof(Strings))]
    [Range(0f, 1f)]
    public IProperty<float> Opacity { get; } = Property.CreateAnimatable(0.5f);

    /// <summary>
    /// Gets the color map texture.
    /// </summary>
    [Display(Name = nameof(Strings.ColorMap), ResourceType = typeof(Strings))]
    public IProperty<TextureSource?> ColorMap { get; } = Property.Create<TextureSource?>(null);

    /// <summary>
    /// Gets the index of refraction for fresnel effect (1.0-2.5).
    /// Higher values increase edge reflection.
    /// </summary>
    [Display(Name = nameof(Strings.IndexOfRefraction), ResourceType = typeof(Strings))]
    [Range(1f, 2.5f)]
    public IProperty<float> IndexOfRefraction { get; } = Property.CreateAnimatable(1.5f);

    /// <summary>
    /// Gets the roughness of the material (0-1).
    /// </summary>
    [Display(Name = nameof(Strings.Roughness), ResourceType = typeof(Strings))]
    [Range(0f, 1f)]
    public IProperty<float> Roughness { get; } = Property.CreateAnimatable(0.1f);

    public partial class Resource
    {
        private IPipeline3D? _pipeline;
        private IDescriptorSet? _descriptorSet;
        private IBuffer? _uniformBuffer;
        private ISampler? _sampler;
        private ITexture2D? _defaultWhiteTexture;

        internal override IPipeline3D? Pipeline => _pipeline;

        /// <summary>
        /// Gets whether this material is transparent and requires forward rendering.
        /// </summary>
        public override bool IsTransparent => true;

        public override void EnsurePipeline(RenderContext3D context)
        {
            if (IsPipelineInitialized)
                return;

            var graphicsContext = context.GraphicsContext;
            var shaderCompiler = context.ShaderCompiler;

            // Create uniform buffer
            _uniformBuffer = graphicsContext.CreateBuffer(
                (ulong)Marshal.SizeOf<TransparentMaterialUBO>(),
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

            // Compile shaders for forward rendering
            var vertexSpirv = shaderCompiler.CompileToSpirv(VertexShaderSource, ShaderStage.Vertex);
            var fragmentSpirv = shaderCompiler.CompileToSpirv(FragmentShaderSource, ShaderStage.Fragment);

            // Create descriptor bindings (UBO + 1 texture sampler)
            var descriptorBindings = new DescriptorBinding[]
            {
                new(0, DescriptorType.UniformBuffer, 1, ShaderStage.Vertex | ShaderStage.Fragment),
                new(1, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment), // colorMap
            };

            // Create pipeline with transparent blend options
            _pipeline = graphicsContext.CreatePipeline3D(
                context.RenderPass,
                vertexSpirv,
                fragmentSpirv,
                descriptorBindings,
                Vertex3D.GetVertexInputDescription(),
                PipelineOptions.Transparent);

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
            ITexture2D? colorTex = ColorMap?.GetTexture(graphicsContext);
            int hasTexture = colorTex != null ? 1 : 0;

            // Update texture binding
            _descriptorSet.UpdateTexture(1, colorTex ?? _defaultWhiteTexture!, _sampler);

            // Calculate primary light data
            Vector3 lightDirection = context.LightDirection;
            Vector3 lightColor = context.LightColor;

            // Update uniform buffer
            var ubo = new TransparentMaterialUBO
            {
                Model = worldMatrix,
                View = context.ViewMatrix,
                Projection = context.ProjectionMatrix,
                CameraPosition = new Vector4(context.CameraPosition, 1.0f),
                BaseColor = new Vector4(
                    Color.R / 255f,
                    Color.G / 255f,
                    Color.B / 255f,
                    Color.A / 255f),
                LightDirection = new Vector4(Vector3.Normalize(lightDirection), 0.0f),
                LightColor = new Vector4(lightColor, 1.0f),
                AmbientColor = new Vector4(context.AmbientColor, 1.0f),
                Opacity = Opacity,
                IndexOfRefraction = IndexOfRefraction,
                Roughness = Roughness,
                HasTexture = hasTexture
            };

            _uniformBuffer.Upload(new ReadOnlySpan<TransparentMaterialUBO>(ref ubo));

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
        /// Uniform buffer object for transparent material (forward rendering).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct TransparentMaterialUBO
        {
            public Matrix4x4 Model;          // 64 bytes
            public Matrix4x4 View;           // 64 bytes
            public Matrix4x4 Projection;     // 64 bytes
            public Vector4 CameraPosition;   // 16 bytes
            public Vector4 BaseColor;        // 16 bytes
            public Vector4 LightDirection;   // 16 bytes
            public Vector4 LightColor;       // 16 bytes
            public Vector4 AmbientColor;     // 16 bytes
            public float Opacity;            // 4 bytes
            public float IndexOfRefraction;  // 4 bytes
            public float Roughness;          // 4 bytes
            public int HasTexture;           // 4 bytes
        }

        /// <summary>
        /// Vertex shader for transparent forward rendering.
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
                vec4 cameraPosition;
                vec4 baseColor;
                vec4 lightDirection;
                vec4 lightColor;
                vec4 ambientColor;
                float opacity;
                float ior;
                float roughness;
                int hasTexture;
            } material;

            layout(location = 0) out vec3 fragWorldPos;
            layout(location = 1) out vec3 fragNormal;
            layout(location = 2) out vec2 fragTexCoord;
            layout(location = 3) out vec3 fragViewDir;

            void main() {
                vec4 worldPos = material.model * vec4(inPosition, 1.0);
                gl_Position = material.projection * material.view * worldPos;

                fragWorldPos = worldPos.xyz;
                fragNormal = mat3(transpose(inverse(material.model))) * inNormal;
                fragTexCoord = inTexCoord;
                fragViewDir = normalize(material.cameraPosition.xyz - worldPos.xyz);
            }
            """;

        /// <summary>
        /// Fragment shader for transparent forward rendering with fresnel effect.
        /// </summary>
        private static string FragmentShaderSource => """
            #version 450

            layout(location = 0) in vec3 fragWorldPos;
            layout(location = 1) in vec3 fragNormal;
            layout(location = 2) in vec2 fragTexCoord;
            layout(location = 3) in vec3 fragViewDir;

            layout(binding = 0) uniform MaterialUBO {
                mat4 model;
                mat4 view;
                mat4 projection;
                vec4 cameraPosition;
                vec4 baseColor;
                vec4 lightDirection;
                vec4 lightColor;
                vec4 ambientColor;
                float opacity;
                float ior;
                float roughness;
                int hasTexture;
            } material;

            layout(binding = 1) uniform sampler2D colorMap;

            layout(location = 0) out vec4 outColor;

            // Fresnel-Schlick approximation
            float fresnelSchlick(float cosTheta, float F0) {
                return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
            }

            void main() {
                vec3 N = normalize(fragNormal);
                vec3 V = normalize(fragViewDir);
                vec3 L = normalize(-material.lightDirection.xyz);
                
                // Calculate Fresnel base reflectance from IOR
                float F0 = pow((material.ior - 1.0) / (material.ior + 1.0), 2.0);
                
                // Fresnel effect - more reflection at glancing angles
                float NdotV = max(dot(N, V), 0.0);
                float fresnel = fresnelSchlick(NdotV, F0);
                
                // Get base color from texture or uniform
                vec4 texColor = material.hasTexture != 0 ? texture(colorMap, fragTexCoord) : vec4(1.0);
                vec4 baseColor = material.baseColor * texColor;
                
                // Simple diffuse lighting
                float NdotL = max(dot(N, L), 0.0);
                vec3 diffuse = baseColor.rgb * material.lightColor.rgb * NdotL;
                
                // Ambient lighting
                vec3 ambient = baseColor.rgb * material.ambientColor.rgb;
                
                // Simple specular (Blinn-Phong)
                vec3 H = normalize(V + L);
                float NdotH = max(dot(N, H), 0.0);
                float shininess = max(2.0 / (material.roughness * material.roughness + 0.001) - 2.0, 1.0);
                float spec = pow(NdotH, shininess);
                vec3 specular = material.lightColor.rgb * spec * fresnel;
                
                // Combine lighting
                vec3 color = ambient + diffuse + specular;
                
                // Apply tone mapping (simple Reinhard)
                color = color / (color + vec3(1.0));
                
                // Gamma correction
                color = pow(color, vec3(1.0 / 2.2));
                
                // Final opacity combines material opacity with fresnel
                // More transparent at center, more opaque/reflective at edges
                float finalOpacity = mix(material.opacity, 1.0, fresnel * 0.5);
                finalOpacity *= baseColor.a;
                
                outColor = vec4(color, finalOpacity);
            }
            """;
    }
}

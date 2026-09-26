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
/// A material that ignores lighting and shows its color and color map as they are, including their alpha.
/// </summary>
/// <remarks>
/// It is drawn in the transparent pass, both sides visible, so 2D content keeps its colors and its transparent
/// areas stay see-through when placed in a 3D scene.
/// </remarks>
[Display(Name = nameof(GraphicsStrings.UnlitMaterial), ResourceType = typeof(GraphicsStrings))]
public sealed partial class UnlitMaterial : Material3D
{
    public UnlitMaterial()
    {
        ScanProperties<UnlitMaterial>();
    }

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.UnlitMaterial_ColorMap), ResourceType = typeof(GraphicsStrings))]
    public IProperty<TextureSource?> ColorMap { get; } = Property.Create<TextureSource?>(null);

    [Display(Name = nameof(GraphicsStrings.Opacity), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 1f), NumberStep(0.1, 0.01)]
    public IProperty<float> Opacity { get; } = Property.CreateAnimatable(1f);

    public partial class Resource
    {
        private IPipeline3D? _pipeline;
        private IRenderPass3D? _pipelineRenderPass;
        private MaterialDrawBindingPool? _drawBindings;
        private ISampler? _sampler;
        private ITexture2D? _defaultWhiteTexture;

        protected internal override IPipeline3D? Pipeline => _pipeline;

        internal MaterialDrawBindingPool? DrawBindings => _drawBindings;

        /// <summary>A color map supplied by the owning object in place of <see cref="ColorMap"/>.</summary>
        internal TextureSource.Resource? ContentMap { get; set; }

        private TextureSource.Resource? EffectiveColorMap => ContentMap ?? ColorMap;

        public override bool IsTransparent => true;

        internal override bool IsDoubleSided => true;

        // The fragment shader discards every pixel once the color's alpha or the opacity reaches zero.
        internal override bool IsInvisible => Opacity <= 0 || Color.A == 0;

        protected internal override IEnumerable<TextureSource.Resource> EnumerateTextureSources()
        {
            if (EffectiveColorMap is { } map)
                yield return map;
        }

        public override void EnsurePipeline(RenderContext3D context)
        {
            if (IsPipelineInitialized && ReferenceEquals(_pipelineRenderPass, context.RenderPass))
                return;

            PostDispose(true);
            IsPipelineInitialized = false;

            var graphicsContext = context.GraphicsContext;
            var shaderCompiler = context.ShaderCompiler;

            _sampler = graphicsContext.CreateSampler(
                SamplerFilter.Linear,
                SamplerFilter.Linear,
                SamplerAddressMode.ClampToEdge,
                SamplerAddressMode.ClampToEdge);
            _defaultWhiteTexture = MaterialGpuResources.Create1x1Texture(graphicsContext, [255, 255, 255, 255]);

            var vertexSpirv = shaderCompiler.CompileToSpirv(VertexShaderSource, ShaderStage.Vertex);
            var fragmentSpirv = shaderCompiler.CompileToSpirv(FragmentShaderSource, ShaderStage.Fragment);

            var descriptorBindings = new DescriptorBinding[]
            {
                new(0, DescriptorType.UniformBuffer, 1, ShaderStage.Vertex | ShaderStage.Fragment),
                new(1, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment),
            };

            // Both faces are drawn so a turned card shows its back. Like other transparent surfaces it does
            // not write depth, so a translucent card never hides another card behind it.
            PipelineOptions options = PipelineOptions.Transparent;
            options.CullMode = CullMode.None;

            _pipeline = graphicsContext.CreatePipeline3D(
                context.RenderPass,
                vertexSpirv,
                fragmentSpirv,
                descriptorBindings,
                Vertex3D.GetVertexInputDescription(),
                options);

            _drawBindings = MaterialDrawBindingPool.Create<UnlitMaterialUBO>(graphicsContext, _pipeline, 1);

            _pipelineRenderPass = context.RenderPass;
            IsPipelineInitialized = true;
        }

        public override void Bind(RenderContext3D context, Object3D.Resource obj, Matrix4x4 worldMatrix)
        {
            if (_pipeline == null || _drawBindings == null || _sampler == null)
                return;

            ITexture2D? colorTex = EffectiveColorMap?.GetTexture(context.GraphicsContext, context.SurfaceDensity);

            // Recorded draws must retain immutable bindings until the backend completes them.
            MaterialDrawBindings bindings = _drawBindings.Acquire();
            bindings.Descriptors.UpdateTexture(1, colorTex ?? _defaultWhiteTexture!, _sampler);

            var ubo = new UnlitMaterialUBO
            {
                Model = worldMatrix,
                View = context.ViewMatrix,
                Projection = context.ProjectionMatrix,
                BaseColor = Color.ToLinearPremultiplied() * Math.Clamp(Opacity, 0f, 1f),
            };

            bindings.Buffer.Upload(new ReadOnlySpan<UnlitMaterialUBO>(ref ubo));

            context.RenderPass.BindPipeline(_pipeline);
            context.RenderPass.BindDescriptorSet(_pipeline, bindings.Descriptors);
            _drawBindings.MarkBound(bindings);
        }

        partial void PostDispose(bool disposing)
        {
            _pipelineRenderPass = null;
            IsPipelineInitialized = false;
            _drawBindings?.Dispose();
            _drawBindings = null;
            _sampler?.Dispose();
            _sampler = null;
            _defaultWhiteTexture?.Dispose();
            _defaultWhiteTexture = null;
            _pipeline?.Dispose();
            _pipeline = null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnlitMaterialUBO
        {
            public Matrix4x4 Model;
            public Matrix4x4 View;
            public Matrix4x4 Projection;
            public Vector4 BaseColor;
        }

        // Both stages bind this block at descriptor binding 0, so they must declare the same fields in
        // the same order as UnlitMaterialUBO.
        private const string MaterialUboSource = """
            layout(binding = 0) uniform MaterialUBO {
                mat4 model;
                mat4 view;
                mat4 projection;
                vec4 baseColor;
            } material;
            """;

        private static string VertexShaderSource => $$"""
            #version 450

            layout(location = 0) in vec3 inPosition;
            layout(location = 1) in vec3 inNormal;
            layout(location = 2) in vec2 inTexCoord;
            layout(location = 3) in vec4 inTangent;

            {{MaterialUboSource}}

            layout(location = 0) out vec2 fragTexCoord;

            void main() {
                gl_Position = material.projection * material.view * material.model * vec4(inPosition, 1.0);
                fragTexCoord = inTexCoord;
            }
            """;

        // The color and the color map are both premultiplied, which is what the transparent pass blends.
        private static string FragmentShaderSource => $$"""
            #version 450

            layout(location = 0) in vec2 fragTexCoord;

            {{MaterialUboSource}}

            layout(binding = 1) uniform sampler2D colorMap;

            layout(location = 0) out vec4 outColor;

            void main() {
                vec4 color = material.baseColor * texture(colorMap, fragTexCoord);
                if (color.a < 1.0 / 255.0) {
                    discard;
                }

                outColor = color;
            }
            """;
    }
}

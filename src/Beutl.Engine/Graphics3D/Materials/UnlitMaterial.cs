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
    public IExpressionProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.UnlitMaterial_ColorMap), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> ColorMap { get; } = Property.CreateExpression<TextureSource?>(null);

    [Display(Name = nameof(GraphicsStrings.Opacity), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 1f), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Opacity { get; } = Property.CreateAnimatable(1f);

    [Display(Name = nameof(GraphicsStrings.AlphaMode), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<AlphaMode> AlphaMode { get; } = Property.CreateExpression(Materials.AlphaMode.Auto);

    [Display(Name = nameof(GraphicsStrings.UnlitMaterial_AlphaCutoff), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 1f), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> AlphaCutoff { get; } = Property.CreateAnimatable(0.5f);

    public partial class Resource
    {
        // The pipelines differ in whether they cull back faces and write depth, so each combination has one.
        private const int DoubleSidedVariantBit = 1;
        private const int WritesDepthVariantBit = 2;

        private readonly IPipeline3D?[] _pipelines = new IPipeline3D?[4];
        private readonly MaterialDrawBindingPool?[] _drawBindings = new MaterialDrawBindingPool?[4];
        private IRenderPass3D? _pipelineRenderPass;
        private ISampler? _sampler;
        private ITexture2D? _defaultWhiteTexture;

        protected internal override IPipeline3D? Pipeline => _pipelines[Variant];

        private int Variant => (IsDoubleSided ? DoubleSidedVariantBit : 0) | (WritesDepth ? WritesDepthVariantBit : 0);

        /// <summary>A color map supplied by the owning object in place of <see cref="ColorMap"/>.</summary>
        internal TextureSource.Resource? ContentMap { get; set; }

        private TextureSource.Resource? EffectiveColorMap => ContentMap ?? ColorMap;

        public override bool IsTransparent => true;

        protected internal override bool IsDoubleSided => ContentMap != null;

        protected internal override bool WritesDepth => AlphaMode switch
        {
            Materials.AlphaMode.Opaque or Materials.AlphaMode.Mask => true,
            Materials.AlphaMode.Blend => false,
            // A card shows see-through 2D content, so it writes no depth. A mesh with an opaque tint does even when
            // textured, so a concave model hides its own far side; texels with no alpha are discarded and write
            // nothing. Translucent texels of a self-overlapping mesh then depend on triangle order, the usual cost
            // of not sorting within a mesh.
            _ => ContentMap == null && Opacity >= 1 && Color.A == 255,
        };

        // The fragment shader discards every pixel once the color's alpha or the opacity falls below what it keeps.
        protected internal override bool IsInvisible => AlphaMode switch
        {
            Materials.AlphaMode.Opaque => false,
            Materials.AlphaMode.Mask => Opacity * Color.A / 255f < AlphaCutoff,
            _ => Opacity <= 0 || Color.A == 0,
        };

        internal override AlphaCutout? Cutout => AlphaMode == Materials.AlphaMode.Mask && EffectiveColorMap is { } map
            ? new AlphaCutout(map, Opacity * Color.A / 255f, AlphaCutoff)
            : null;

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

            _sampler = MaterialGpuResources.CreateLinearClampSampler(graphicsContext);
            _defaultWhiteTexture = MaterialGpuResources.Create1x1Texture(graphicsContext, [255, 255, 255, 255]);

            var vertexSpirv = shaderCompiler.CompileToSpirv(VertexShaderSource, ShaderStage.Vertex);
            var fragmentSpirv = shaderCompiler.CompileToSpirv(FragmentShaderSource, ShaderStage.Fragment);

            var descriptorBindings = MaterialGpuResources.CreateDescriptorBindings(textureCount: 1);

            for (int variant = 0; variant < _pipelines.Length; variant++)
            {
                PipelineOptions options = PipelineOptions.Transparent;
                options.CullMode = (variant & DoubleSidedVariantBit) != 0 ? CullMode.None : CullMode.Back;
                options.DepthWriteEnabled = (variant & WritesDepthVariantBit) != 0;

                IPipeline3D pipeline = graphicsContext.CreatePipeline3D(
                    context.RenderPass,
                    vertexSpirv,
                    fragmentSpirv,
                    descriptorBindings,
                    Vertex3D.GetVertexInputDescription(),
                    options);
                _pipelines[variant] = pipeline;
                _drawBindings[variant] = MaterialDrawBindingPool.Create<UnlitMaterialUBO>(graphicsContext, pipeline, 1);
            }

            _pipelineRenderPass = context.RenderPass;
            IsPipelineInitialized = true;
        }

        public override void Bind(RenderContext3D context, Object3D.Resource obj, Matrix4x4 worldMatrix)
        {
            int variant = Variant;
            IPipeline3D? pipeline = _pipelines[variant];
            MaterialDrawBindingPool? drawBindings = _drawBindings[variant];
            if (pipeline == null || drawBindings == null || _sampler == null)
                return;

            // An opaque material ignores the map's alpha, so what the map stores under alpha 0 shows too.
            ITexture2D? straightColorTex = AlphaMode == Materials.AlphaMode.Opaque
                ? EffectiveColorMap?.GetStraightColorTexture(context.GraphicsContext)
                : null;
            ITexture2D? colorTex = straightColorTex
                ?? EffectiveColorMap?.GetTexture(context.GraphicsContext, context.SurfaceDensity);

            // Recorded draws must retain immutable bindings until the backend completes them.
            MaterialDrawBindings bindings = drawBindings.Acquire();
            bindings.Descriptors.UpdateTexture(1, colorTex ?? _defaultWhiteTexture!, _sampler);

            var ubo = new UnlitMaterialUBO
            {
                Model = worldMatrix,
                View = context.ViewMatrix,
                Projection = context.ProjectionMatrix,
                // Alpha means nothing to an opaque material, whose map's alpha is ignored in the shader as well.
                BaseColor = AlphaMode == Materials.AlphaMode.Opaque
                    ? new Color(255, Color.R, Color.G, Color.B).ToLinearPremultiplied()
                    : Color.ToLinearPremultiplied() * Opacity,
                Alpha = AlphaMode switch
                {
                    Materials.AlphaMode.Opaque => new Vector4(0f, straightColorTex != null ? 2f : 1f, 0f, 0f),
                    Materials.AlphaMode.Mask => new Vector4(AlphaCutoff, 1f, 0f, 0f),
                    _ => new Vector4(1f / 255f, 0f, 0f, 0f),
                },
            };

            drawBindings.UploadAndBind(bindings, in ubo, context.RenderPass, pipeline);
        }

        partial void PostDispose(bool disposing)
        {
            _pipelineRenderPass = null;
            IsPipelineInitialized = false;
            for (int variant = 0; variant < _pipelines.Length; variant++)
            {
                _drawBindings[variant]?.Dispose();
                _drawBindings[variant] = null;
                _pipelines[variant]?.Dispose();
                _pipelines[variant] = null;
            }

            _sampler?.Dispose();
            _sampler = null;
            _defaultWhiteTexture?.Dispose();
            _defaultWhiteTexture = null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnlitMaterialUBO
        {
            public Matrix4x4 Model;
            public Matrix4x4 View;
            public Matrix4x4 Projection;
            public Vector4 BaseColor;
            public Vector4 Alpha;
        }

        // Both stages bind this block at descriptor binding 0, so they must declare the same fields in
        // the same order as UnlitMaterialUBO.
        private const string MaterialUboSource = """
            layout(binding = 0) uniform MaterialUBO {
                mat4 model;
                mat4 view;
                mat4 projection;
                vec4 baseColor;
                vec4 alpha; // x: the alpha a pixel needs to be kept, y: 1 when kept pixels are opaque, 2 when also not premultiplied.
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
                if (color.a < material.alpha.x) {
                    discard;
                }

                if (material.alpha.y == 2.0) {
                    outColor = vec4(color.rgb, 1.0);
                } else {
                    outColor = material.alpha.y != 0.0 ? vec4(color.rgb / max(color.a, 1.0 / 255.0), 1.0) : color;
                }
            }
            """;
    }
}

using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Nodes;
using Beutl.Graphics3D.Textures;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics3D.Skyboxes;

/// <summary>A background at infinity in a 3D scene.</summary>
[Display(Name = nameof(GraphicsStrings.Scene3D_Skybox), ResourceType = typeof(GraphicsStrings))]
public abstract partial class Skybox3D : EngineObject
{
    protected Skybox3D()
    {
        ScanProperties<Skybox3D>();
    }

    /// <summary>Gets the Euler angles (X pitch, Y yaw, Z roll) in Y-up environment space.</summary>
    [Display(Name = nameof(GraphicsStrings.Rotation), ResourceType = typeof(GraphicsStrings))]
    [Unit(PropertyUnit.Degrees)]
    public IExpressionProperty<Vector3> Rotation { get; } = Property.CreateAnimatable(Vector3.Zero);

    /// <summary>Gets the RGB tint applied to the skybox.</summary>
    [Display(Name = nameof(GraphicsStrings.Skybox3D_Tint), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Tint { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.Skybox3D_Intensity), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, float.MaxValue), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Intensity { get; } = Property.CreateAnimatable(1f);

    [Display(Name = nameof(GraphicsStrings.Opacity), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 1f), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Opacity { get; } = Property.CreateAnimatable(1f);

    public abstract partial class Resource
    {
        private SkyboxPipeline? _pipeline;

        /// <summary>Enumerates the texture sources needed before the skybox is drawn.</summary>
        protected internal virtual IEnumerable<TextureSource.Resource> EnumerateTextureSources() => [];

        /// <summary>
        /// Prepares the bindings for a fullscreen triangle in a single-sample, depthless RGBA16Float pass.
        /// Returns null when the skybox has no image to draw. The fragment shader outputs linear premultiplied RGBA.
        /// </summary>
        /// <param name="context">The graphics context.</param>
        /// <param name="shaderCompiler">The compiler for this backend.</param>
        /// <param name="renderPass">The pass the pipeline will be drawn in. The pass has not begun.</param>
        /// <param name="clipToSky">
        /// Transforms far-plane clip coordinates into the skybox's local Y-up space, with rotation applied.
        /// Normalize the XYZ result for the sampling direction.
        /// </param>
        /// <param name="surfaceDensity">The device pixels per logical unit for rasterizing drawable textures.</param>
        protected internal abstract (IPipeline3D Pipeline, IDescriptorSet Descriptors)? PrepareDraw(
            IGraphicsContext context, IShaderCompiler shaderCompiler, IRenderPass3D renderPass,
            Matrix4x4 clipToSky, float surfaceDensity);

        private protected SkyboxUniforms GetUniforms(Matrix4x4 clipToSky)
            => new()
            {
                ClipToSky = clipToSky,
                TintAndOpacity = new Vector4(
                    Tint.ToLinear().AsVector3() * MathF.Max(0f, Intensity), Math.Clamp(Opacity, 0f, 1f)),
            };

        private protected SkyboxPipeline GetPipeline<TUniforms>(
            IGraphicsContext context, IShaderCompiler compiler, IRenderPass3D renderPass,
            string fragmentShader, SamplerAddressMode? textureAddressModeU = null)
            where TUniforms : unmanaged
        {
            if (_pipeline is { } existing && existing.Matches(context, renderPass))
                return existing;

            var replacement = SkyboxPipeline.Create<TUniforms>(
                context, compiler, renderPass, fragmentShader, textureAddressModeU);
            SkyboxPipeline? previous = _pipeline;
            _pipeline = replacement;
            previous?.Dispose();
            return replacement;
        }

        partial void PostDispose(bool disposing)
        {
            _pipeline?.Dispose();
            _pipeline = null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private protected struct SkyboxUniforms
        {
            public Matrix4x4 ClipToSky;
            public Vector4 TintAndOpacity;
        }

        private protected const string FragmentHeader = """
            #version 450

            layout(location = 0) in vec2 ndc;
            layout(location = 0) out vec4 outColor;

            struct SkyboxUniforms {
                mat4 clipToSky;
                vec4 tintAndOpacity;
            };

            vec3 skyDirection(SkyboxUniforms sky) {
                return normalize((sky.clipToSky * vec4(ndc, 1.0, 1.0)).xyz);
            }

            vec4 finishSky(vec4 premul, SkyboxUniforms sky) {
                float opacity = sky.tintAndOpacity.a;
                vec3 rgb = min(premul.rgb * opacity * sky.tintAndOpacity.rgb, vec3(65504.0));
                return vec4(rgb, clamp(premul.a * opacity, 0.0, 1.0));
            }

            """;

        private protected sealed class SkyboxPipeline(
            IGraphicsContext context, IRenderPass3D renderPass, IPipeline3D pipeline,
            MaterialDrawBindingPool bindings, ISampler? sampler) : IDisposable
        {
            private const string VertexShader = """
                #version 450
                layout(location = 0) out vec2 ndc;

                void main() {
                    vec2 positions[3] = vec2[](vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
                    ndc = positions[gl_VertexIndex];
                    gl_Position = vec4(ndc, 0.0, 1.0);
                }
                """;

            public ISampler Sampler => sampler!;

            public bool Matches(IGraphicsContext otherContext, IRenderPass3D otherPass)
                => ReferenceEquals(context, otherContext) && ReferenceEquals(renderPass, otherPass);

            public static SkyboxPipeline Create<TUniforms>(
                IGraphicsContext context, IShaderCompiler compiler, IRenderPass3D renderPass,
                string fragmentShader, SamplerAddressMode? textureAddressModeU)
                where TUniforms : unmanaged
            {
                uint textureCount = textureAddressModeU.HasValue ? 1u : 0u;
                IPipeline3D? pipeline = null;
                ISampler? sampler = null;
                try
                {
                    pipeline = context.CreatePipeline3D(
                        renderPass,
                        compiler.CompileToSpirv(VertexShader, ShaderStage.Vertex),
                        compiler.CompileToSpirv(fragmentShader, ShaderStage.Fragment),
                        MaterialGpuResources.CreateDescriptorBindings(textureCount),
                        VertexInputDescription.Empty,
                        PipelineOptions.Fullscreen);
                    if (textureAddressModeU is { } addressMode)
                    {
                        sampler = context.CreateSampler(
                            SamplerFilter.Linear, SamplerFilter.Linear, addressMode, SamplerAddressMode.ClampToEdge);
                    }

                    var bindings = MaterialDrawBindingPool.Create<TUniforms>(context, pipeline, textureCount);
                    return new SkyboxPipeline(context, renderPass, pipeline, bindings, sampler);
                }
                catch
                {
                    RenderNode3D.DisposeResources([sampler, pipeline]);
                    throw;
                }
            }

            public MaterialDrawBindings AcquireBindings() => bindings.Acquire();

            public (IPipeline3D Pipeline, IDescriptorSet Descriptors) PrepareDraw<TUniforms>(
                MaterialDrawBindings drawBindings, in TUniforms uniforms)
                where TUniforms : unmanaged
            {
                drawBindings.Buffer.Upload(new ReadOnlySpan<TUniforms>(in uniforms));
                // Each skybox draw is its own pass. The previous pass can no longer rebind its descriptors.
                bindings.MarkBound(drawBindings);
                return (pipeline, drawBindings.Descriptors);
            }

            public void Dispose() => RenderNode3D.DisposeResources([bindings, sampler, pipeline]);
        }
    }
}

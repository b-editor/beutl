using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Textures;
using Beutl.Language;

namespace Beutl.Graphics3D.Skyboxes;

/// <summary>A skybox sampled from a 360-degree panorama.</summary>
[Display(Name = nameof(GraphicsStrings.EquirectangularSkybox), ResourceType = typeof(GraphicsStrings))]
public sealed partial class EquirectangularSkybox : Skybox3D
{
    public EquirectangularSkybox()
    {
        ScanProperties<EquirectangularSkybox>();
    }

    /// <summary>Gets the panorama with the zenith at the top and the nadir at the bottom.</summary>
    [Display(Name = nameof(GraphicsStrings.EquirectangularSkybox_Texture), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextureSource?> Texture { get; } = Property.CreateExpression<TextureSource?>();

    public partial class Resource
    {
        protected internal override IEnumerable<TextureSource.Resource> EnumerateTextureSources()
            => Texture is { } texture ? [texture] : [];

        protected internal override (IPipeline3D Pipeline, IDescriptorSet Descriptors)? PrepareDraw(
            IGraphicsContext context, IShaderCompiler shaderCompiler, IRenderPass3D renderPass,
            Matrix4x4 clipToSky, float surfaceDensity)
        {
            if (Texture is not { IsEnabled: true } source
                || source.GetTexture(context, surfaceDensity) is not { } texture)
                return null;

            texture.PrepareForSampling();
            var pipeline = GetPipeline<SkyboxUniforms>(
                context, shaderCompiler, renderPass, FragmentShader, SamplerAddressMode.Repeat);
            var bindings = pipeline.AcquireBindings();
            bindings.Descriptors.UpdateTexture(1, texture, pipeline.Sampler);
            return pipeline.PrepareDraw(bindings, GetUniforms(clipToSky));
        }

        private const string FragmentShader = FragmentHeader + """
            layout(std140, binding = 0) uniform SkyboxUBO { SkyboxUniforms sky; };
            layout(binding = 1) uniform sampler2D panorama;

            void main() {
                const float PI = 3.14159265359;
                vec3 dir = skyDirection(sky);
                // +X is centered, +Z lies to its right, and -X is the wrapping seam.
                float longitude = dot(dir.xz, dir.xz) > 0.0 ? atan(dir.z, dir.x) : 0.0;
                vec2 uv = vec2(0.5 + longitude / (2.0 * PI), acos(clamp(dir.y, -1.0, 1.0)) / PI);
                outColor = finishSky(texture(panorama, uv), sky);
            }
            """;
    }
}

using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

/// <summary>Refracts the source through a two-channel wave field in composition coordinates.</summary>
[Display(Name = nameof(GraphicsStrings.WaveRefraction), ResourceType = typeof(GraphicsStrings))]
public sealed partial class WaveRefraction : FilterEffect
{
    public WaveRefraction() => ScanProperties<WaveRefraction>();

    /// <summary>The maximum displacement along either axis, in composition units.</summary>
    [Display(Name = nameof(GraphicsStrings.WaveRefraction_Amount), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Amount { get; } = Property.CreateAnimatable(8f);

    [Display(Name = nameof(GraphicsStrings.WaveRefraction_Wavelength), ResourceType = typeof(GraphicsStrings))]
    [Range(float.Epsilon, float.MaxValue)]
    public IExpressionProperty<float> Wavelength { get; } = Property.CreateAnimatable(64f);

    /// <summary>The wave phase in degrees. Animate this value to move the waves deterministically across seeks.</summary>
    [Display(Name = nameof(GraphicsStrings.WaveRefraction_Phase), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Phase { get; } = Property.CreateAnimatable(0f);

    private static readonly ShaderProgram s_field = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 1, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { vec4 value; } pc;
        layout(location = 0) out vec2 field;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z - src.logical.xy;
            field = sin(position.yx / pc.value.y * 6.28318530718 + pc.value.z);
        }
        """));

    private static readonly ShaderProgram s_refract = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1) uniform sampler2D field;
        layout(binding = 2, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 3, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { vec4 value; } pc;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            // The field and output have identical raster extents. The field is numeric data, not color.
            vec2 offset = texelFetch(field, ivec2(gl_FragCoord.xy), 0).rg * pc.value.x;
            color = texture(image, (position + offset - src.raster.xy) / src.raster.zw);
        }
        """));

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (!float.IsFinite(Amount) || Amount <= 0 || !float.IsFinite(Wavelength) || Wavelength <= 0
                || !float.IsFinite(Phase))
                return;

            context.ShaderGraph(new Vector4(Amount, Wavelength, (Phase % 360) * (MathF.PI / 180), 0),
                static (graph, values) =>
                {
                    ShaderImage field = graph.Image2D("wave-field", graph.Source, TextureFormat.RG32Float, padding: values.X);
                    ShaderImage output = graph.Image2D("output", field);
                    graph.Fullscreen("wave-field", s_field, pass =>
                    {
                        pass.ImageParameters(0, graph.Source);
                        pass.ImageParameters(1, field);
                        pass.PushConstants(values);
                        pass.PartialRendering();
                        pass.ColorAttachment(0, field);
                    });
                    graph.Fullscreen("refract", s_refract, pass =>
                    {
                        pass.SampledImage(0, graph.Source);
                        pass.SampledImage(1, field, SamplerState.NearestClamp);
                        pass.ImageParameters(2, graph.Source);
                        pass.ImageParameters(3, output);
                        pass.PushConstants(values);
                        pass.InputRegion(0, new(Matrix.Identity, values.X));
                        pass.InputRegion(1, new(Matrix.Identity));
                        pass.ColorAttachment(0, output);
                    });
                    return graph.Output(output);
                });
        }
    }
}

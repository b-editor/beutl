using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Makes details stand out by adding back the difference between a pixel's lightness and the blurred lightness around
/// it (an unsharp mask). The color is only made lighter or darker, so edges get no colored fringes, and only visible
/// pixels count as neighbours, so the edge of a clip gets no halo and keeps its shape.
/// </summary>
[Display(Name = nameof(GraphicsStrings.Sharpen), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Sharpen : FilterEffect
{
    public Sharpen() => ScanProperties<Sharpen>();

    [Display(Name = nameof(GraphicsStrings.Amount), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Amount { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Radius), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Radius { get; } = Property.CreateAnimatable(3f);

    [Display(Name = nameof(GraphicsStrings.Sharpen_Threshold), Description = nameof(GraphicsStrings.Sharpen_Threshold_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Threshold { get; } = Property.CreateAnimatable(0f);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (!float.IsFinite(Amount) || Amount <= 0 || !float.IsFinite(Radius) || Radius <= 0 || !float.IsFinite(Threshold))
                return;

            context.ShaderGraph((Radius, Amount: Amount / 100, Threshold: Threshold / 100),
                static (graph, values) =>
                {
                    ShaderImage lightness = graph.Image2D("lightness", graph.Source);
                    LightEffectPasses.ImagePass(graph, "lightness", s_lightness, graph.Source, lightness);
                    ShaderImage blurred = LightEffectPasses.GaussianBlur(graph, lightness, values.Radius);
                    ShaderImage output = graph.Image2D("output", graph.Source);
                    graph.Fullscreen("sharpen", s_sharpen, pass =>
                    {
                        pass.SampledImage(0, graph.Source);
                        pass.SampledImage(1, lightness);
                        pass.SampledImage(2, blurred);
                        pass.ImageParameters(3, graph.Source);
                        pass.ImageParameters(4, lightness);
                        pass.ImageParameters(5, blurred);
                        pass.ImageParameters(6, output);
                        pass.PushConstants(new Vector2(values.Amount, values.Threshold));
                        pass.InputRegion(0, ShaderInputRegion.Identity);
                        pass.InputRegion(1, ShaderInputRegion.Identity);
                        pass.InputRegion(2, ShaderInputRegion.Identity);
                        pass.ColorAttachment(0, output);
                    });
                    return graph.Output(output);
                });
        }
    }

    // Gamma encoded, so that the two sides of an edge move about equally to the eye and a threshold means the same in
    // the shadows and the highlights. It is premultiplied like a color, so what cannot be seen weighs nothing in the blur.
    private static readonly ShaderProgram s_lightness = ShaderProgram.Fullscreen(new ShaderSource($$"""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(location = 0) out vec4 lightness;
        {{GlslSnippets.Luminance}}
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            vec4 color = texture(image, (position - src.raster.xy) / src.raster.zw);
            lightness = vec4(gammaLightness(color) * color.a, color.a, 0, 0);
        }
        """));

    // A pixel's own lightness comes from the image the blur read, so rounding to half floats cannot make a flat color
    // differ from its surroundings.
    private static readonly ShaderProgram s_sharpen = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1) uniform sampler2D lightness;
        layout(binding = 2) uniform sampler2D blurred;
        layout(binding = 3, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 4, std140) uniform Lightness { vec4 logical; vec4 raster; vec4 size; } own;
        layout(binding = 5, std140) uniform Blurred { vec4 logical; vec4 raster; vec4 size; } around;
        layout(binding = 6, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { float amount; float threshold; } pc;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            color = texture(image, (position - src.raster.xy) / src.raster.zw);
            vec4 center = texture(lightness, (position - own.raster.xy) / own.raster.zw);
            vec4 mean = texture(blurred, (position - around.raster.xy) / around.raster.zw);
            if (center.r <= 0 || mean.g <= 0) return;

            float value = center.r / center.g;
            float difference = value - mean.r / mean.g;
            float detail = sign(difference) * max(abs(difference) - pc.threshold, 0);
            color.rgb *= pow(max(value + detail * pc.amount, 0) / value, 2.2);
        }
        """));
}

using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

/// <summary>Extracts highlights, blurs them in two directions, and composites a linear-light glow.</summary>
[Display(Name = nameof(GraphicsStrings.Bloom), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Bloom : FilterEffect
{
    public Bloom() => ScanProperties<Bloom>();

    /// <summary>The glow's sampling radius in composition units.</summary>
    [Display(Name = nameof(GraphicsStrings.Radius), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Radius { get; } = Property.CreateAnimatable(12f);

    /// <summary>The straight, linear luminance above which pixels contribute to the glow.</summary>
    [Display(Name = nameof(GraphicsStrings.Bloom_Threshold), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Threshold { get; } = Property.CreateAnimatable(0.5f);

    [Display(Name = nameof(GraphicsStrings.Bloom_Intensity), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Intensity { get; } = Property.CreateAnimatable(1f);

    /// <summary>Masks the glow with the original alpha instead of extending the image silhouette.</summary>
    [Display(Name = nameof(GraphicsStrings.Bloom_PreserveAlpha), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> PreserveAlpha { get; } = Property.CreateAnimatable(false);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (!float.IsFinite(r.Radius) || r.Radius < 0 || !float.IsFinite(r.Threshold) || r.Threshold < 0
            || !float.IsFinite(r.Intensity) || r.Intensity <= 0)
            return;

        context.ShaderGraph(new Vector4(r.Radius, r.Threshold, r.Intensity, r.PreserveAlpha ? 1 : 0),
            static (graph, values) =>
            {
                ShaderImage bright = graph.Image2D("bright", graph.Source, padding: values.X);
                ShaderImage mask = graph.Image2D("mask", bright, TextureFormat.R16Float);
                ShaderImage horizontal = graph.Image2D("horizontal", bright);
                ShaderImage glow = graph.Image2D("glow", bright);
                ShaderImage output = graph.Image2D("output", graph.Source, padding: values.W == 0 ? values.X : 0);
                graph.Fullscreen("extract", s_extract, pass =>
                {
                    pass.SampledImage(0, graph.Source);
                    pass.ImageParameters(1, graph.Source);
                    pass.ImageParameters(2, bright);
                    pass.PushConstants(values);
                    pass.InputRegion(0, new(Matrix.Identity));
                    pass.ColorAttachment(0, bright);
                    pass.ColorAttachment(1, mask);
                });
                Blur(graph, "blur-horizontal", bright, horizontal, new(values.X, 1, 0, 0));
                Blur(graph, "blur-vertical", horizontal, glow, new(values.X, 0, 1, 0));
                graph.Fullscreen("composite", s_composite, pass =>
                {
                    pass.SampledImage(0, graph.Source);
                    pass.SampledImage(1, glow);
                    pass.SampledImage(2, mask);
                    pass.ImageParameters(3, graph.Source);
                    pass.ImageParameters(4, glow);
                    pass.ImageParameters(5, output);
                    pass.PushConstants(values);
                    pass.InputRegion(0, new(Matrix.Identity));
                    pass.InputRegion(1, new(Matrix.Identity));
                    pass.InputRegion(2, new(Matrix.Identity));
                    pass.ColorAttachment(0, output);
                });
                return graph.Output(output);
            });
    }

    private static void Blur(ShaderGraphBuilder graph, string name, ShaderImage source, ShaderImage output, Vector4 values)
        => graph.Fullscreen(name, s_blur, pass =>
        {
            pass.SampledImage(0, source);
            pass.ImageParameters(1, source);
            pass.ImageParameters(2, output);
            pass.PushConstants(values);
            pass.InputRegion(0, new(Matrix.Identity, values.X));
            pass.ColorAttachment(0, output);
        });

    private static readonly ShaderProgram s_extract = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { vec4 value; } pc;
        layout(location = 0) out vec4 bright;
        layout(location = 1) out float mask;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            vec4 color = texture(image, (position - src.raster.xy) / src.raster.zw);
            float luminance = color.a > 0 ? dot(color.rgb / color.a, vec3(0.2126, 0.7152, 0.0722)) : 0;
            float weight = luminance > 0 ? max(luminance - pc.value.y, 0) / luminance : 0;
            bright = color * weight;
            mask = color.a;
        }
        """));

    // A fixed quadrature in logical coordinates keeps the footprint and cost independent of render density.
    private static readonly ShaderProgram s_blur = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { vec4 value; } pc;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            color = vec4(0);
            float total = 0;
            for (int i = -16; i <= 16; i++) {
                float x = float(i) / 16;
                float weight = exp(-4.5 * x * x);
                vec2 samplePosition = position + pc.value.yz * (x * pc.value.x);
                color += texture(image, (samplePosition - src.raster.xy) / src.raster.zw) * weight;
                total += weight;
            }
            color /= total;
        }
        """));

    private static readonly ShaderProgram s_composite = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1) uniform sampler2D blurred;
        layout(binding = 2) uniform sampler2D mask;
        layout(binding = 3, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 4, std140) uniform Glow { vec4 logical; vec4 raster; vec4 size; } glow;
        layout(binding = 5, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { vec4 value; } pc;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            vec4 original = texture(image, (position - src.raster.xy) / src.raster.zw);
            vec2 uv = (position - glow.raster.xy) / glow.raster.zw;
            vec4 light = texture(blurred, uv) * pc.value.z;
            if (pc.value.w != 0) {
                color = vec4(original.rgb + light.rgb * texture(mask, uv).r, original.a);
            } else {
                color = vec4(original.rgb + light.rgb, original.a + clamp(light.a, 0, 1) * (1 - original.a));
            }
        }
        """));
}

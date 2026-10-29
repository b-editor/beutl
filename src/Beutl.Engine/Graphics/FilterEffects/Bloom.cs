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
    [Display(Name = nameof(GraphicsStrings.Radius), Description = nameof(GraphicsStrings.Bloom_Radius_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Radius { get; } = Property.CreateAnimatable(12f);

    /// <summary>The straight, linear luminance at the center of the highlight threshold.</summary>
    [Display(Name = nameof(GraphicsStrings.Bloom_Threshold), Description = nameof(GraphicsStrings.Bloom_Threshold_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Threshold { get; } = Property.CreateAnimatable(0.5f);

    /// <summary>The transition's half-width as a percentage of Threshold. Zero preserves the hard threshold.</summary>
    [Display(Name = nameof(GraphicsStrings.Bloom_SoftKnee), Description = nameof(GraphicsStrings.Bloom_SoftKnee_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> SoftKnee { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Bloom_Intensity), Description = nameof(GraphicsStrings.Bloom_Intensity_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Intensity { get; } = Property.CreateAnimatable(1f);

    /// <summary>Masks the glow with the original alpha instead of extending the image silhouette.</summary>
    [Display(Name = nameof(GraphicsStrings.Bloom_PreserveAlpha), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> PreserveAlpha { get; } = Property.CreateAnimatable(false);

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

    // Sample count depends on working scale, but no longer grows with the logical radius.
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
            int steps = max(16, int(ceil(pc.value.x * src.size.z)));
            for (int i = -steps; i <= steps; i++) {
                float x = float(i) / float(steps);
                float weight = exp(-4.5 * x * x);
                vec2 samplePosition = position + pc.value.yz * (x * pc.value.x);
                color += texture(image, (samplePosition - src.raster.xy) / src.raster.zw) * weight;
                total += weight;
            }
            color /= total;
        }
        """));

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (!float.IsFinite(Radius) || Radius < 0 || !float.IsFinite(Threshold) || Threshold < 0
                || !float.IsFinite(SoftKnee) || !float.IsFinite(Intensity) || Intensity <= 0)
                return;

            context.ShaderGraph((Radius, Threshold, SoftKnee: Math.Clamp(SoftKnee, 0, 100) / 100,
                    Intensity, PreserveAlpha),
                static (graph, values) =>
                {
                    ShaderImage bright = graph.Image2D("bright", graph.Source, padding: values.Radius);
                    ShaderImage mask = graph.Image2D("mask", bright, TextureFormat.R16Float);
                    ShaderImage output = graph.Image2D("output", graph.Source, padding: values.PreserveAlpha ? 0 : values.Radius);
                    LightEffectPasses.ExtractHighlights(graph, graph.Source, bright, mask, values.Threshold, values.SoftKnee);
                    // Reduce in 2x steps so even a one-pixel highlight survives the filtering.
                    // The final blur radius stays at most 16 texels per unit of working scale.
                    ShaderImage reduced = bright;
                    for (int level = 1; values.Radius * reduced.ResolutionScale > 16; level++)
                    {
                        ShaderImage source = reduced;
                        reduced = graph.Image2D($"downsample-{level}", source, resolutionScale: source.ResolutionScale / 2);
                        LightEffectPasses.Downsample(graph, source, reduced);
                    }
                    ShaderImage horizontal = graph.Image2D("horizontal", reduced, resolutionScale: reduced.ResolutionScale);
                    ShaderImage glow = graph.Image2D("glow", reduced, resolutionScale: reduced.ResolutionScale);
                    Blur(graph, "blur-horizontal", reduced, horizontal, new(values.Radius, 1, 0, 0));
                    Blur(graph, "blur-vertical", horizontal, glow, new(values.Radius, 0, 1, 0));
                    LightEffectPasses.Composite(graph, graph.Source, glow, mask, output, values.Intensity, values.PreserveAlpha);
                    return graph.Output(output);
                });
        }
    }
}

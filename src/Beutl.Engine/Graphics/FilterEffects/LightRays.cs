using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

/// <summary>Extends extracted highlights away from a center while preserving the original image.</summary>
[Display(Name = nameof(GraphicsStrings.LightRays), ResourceType = typeof(GraphicsStrings))]
public sealed partial class LightRays : FilterEffect
{
    public LightRays() => ScanProperties<LightRays>();

    [Display(Name = nameof(GraphicsStrings.RadialBlur_Center), Description = nameof(GraphicsStrings.LightRays_Center_Description),
        ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> Center { get; } = Property.CreateAnimatable(RelativePoint.Center);

    /// <summary>Outward extension as a percentage of the distance from the center.</summary>
    [Display(Name = nameof(GraphicsStrings.LightRays_Length), Description = nameof(GraphicsStrings.LightRays_Length_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Length { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(GraphicsStrings.LightRays_Decay), Description = nameof(GraphicsStrings.LightRays_Decay_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Decay { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(GraphicsStrings.Bloom_Intensity), Description = nameof(GraphicsStrings.Bloom_Intensity_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Intensity { get; } = Property.CreateAnimatable(1f);

    [Display(Name = nameof(GraphicsStrings.Bloom_Threshold), Description = nameof(GraphicsStrings.Bloom_Threshold_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue), NumberStep(0.1, 0.01)]
    public IExpressionProperty<float> Threshold { get; } = Property.CreateAnimatable(0.5f);

    [Display(Name = nameof(GraphicsStrings.Bloom_SoftKnee), Description = nameof(GraphicsStrings.Bloom_SoftKnee_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> SoftKnee { get; } = Property.CreateAnimatable(50f);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (!float.IsFinite(r.Length) || r.Length <= 0 || !float.IsFinite(r.Intensity) || r.Intensity <= 0
            || !float.IsFinite(r.Center.Point.X) || !float.IsFinite(r.Center.Point.Y)
            || !float.IsFinite(r.Threshold) || r.Threshold < 0 || !float.IsFinite(r.SoftKnee) || !float.IsFinite(r.Decay))
            return;

        context.ShaderGraph((r.Center, InverseStretch: 1 / (1 + r.Length / 100),
                Decay: Math.Clamp(r.Decay, 0, 100) / 25, r.Intensity, r.Threshold,
                SoftKnee: Math.Clamp(r.SoftKnee, 0, 100) / 100),
            static (graph, values) =>
            {
                ShaderImage bright = graph.Image2D("bright", graph.Source);
                ShaderImage mask = graph.Image2D("mask", bright, TextureFormat.R16Float);
                LightEffectPasses.ExtractHighlights(graph, graph.Source, bright, mask, values.Threshold, values.SoftKnee);

                // Prefilter before halving the raster density so isolated highlights survive every grid phase.
                ShaderImage reduced = graph.Image2D("downsample", bright, resolutionScale: 0.5f);
                LightEffectPasses.Downsample(graph, bright, reduced);
                ShaderImage previous = reduced;
                float stretch = 1 / values.InverseStretch;
                // Five radix-eight passes integrate 32768 positions with only 40 reads per light pixel.
                // Zooms compose multiplicatively, so use equally spaced positions in log distance.
                float step = 1f / 32768;
                for (int level = 0; level < 5; level++, step *= 8)
                {
                    ShaderImage source = previous;
                    ShaderImage rays = graph.Image2D($"rays-{level}", graph.Source, resolutionScale: 0.5f);
                    rays.BoundsTransforms = [Matrix.CreateScale(stretch, stretch)];
                    rays.BoundsOrigin = values.Center;
                    float logStep = MathF.Log(stretch) * step;
                    var constants = new Vector4(MathF.Exp(-logStep), MathF.Exp(-values.Decay * step),
                        level == 0 ? MathF.Exp(-logStep / 2) : 1, 0);
                    graph.Fullscreen(rays.Name, s_rays, pass =>
                    {
                        pass.SampledImage(0, source);
                        pass.ImageParameters(1, source);
                        pass.ImageParameters(2, rays);
                        pass.PushConstants((Vector4.Zero, constants));
                        pass.BoundsOriginConstants(values.Center, 0);
                        // Keep the whole input: intermediate zooms can reach outside a requested output tile.
                        pass.ColorAttachment(0, rays);
                    });
                    previous = rays;
                }
                ShaderImage output = graph.Image2D("output", previous);
                LightEffectPasses.Composite(graph, graph.Source, previous, mask, output,
                    values.Intensity / (values.Decay + 1), preserveAlpha: false);
                return graph.Output(output);
            });
    }

    private static readonly ShaderProgram s_rays = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { vec4 center; vec4 step; } pc;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            color = vec4(0);
            float total = 0;
            float factor = pc.step.z;
            float weight = 1;
            for (int i = 0; i < 8; i++) {
                vec2 p = pc.center.xy + (position - pc.center.xy) * factor;
                color += textureLod(image, (p - src.raster.xy) / src.raster.zw, 0) * weight;
                total += weight;
                factor *= pc.step.x;
                weight *= pc.step.y;
            }
            color /= total;
        }
        """));
}

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

                ShaderImage rays = graph.Image2D("rays", graph.Source);
                // Share the same float32 transform and complete-input origin with rendering, as RadialBlur does.
                float stretch = 1 / values.InverseStretch;
                rays.BoundsTransforms = [Matrix.CreateScale(stretch, stretch)];
                rays.BoundsOrigin = values.Center;
                graph.Fullscreen("rays", s_rays, pass =>
                {
                    pass.SampledImage(0, bright);
                    pass.ImageParameters(1, bright);
                    pass.ImageParameters(2, rays);
                    pass.PushConstants(new Vector4(0, 0, values.InverseStretch, values.Decay));
                    pass.BoundsOriginConstants(values.Center, 0);
                    // Rays can sample anywhere between the output pixel and the center: retain the full source.
                    pass.ColorAttachment(0, rays);
                });
                ShaderImage output = graph.Image2D("output", rays);
                LightEffectPasses.Composite(graph, graph.Source, rays, mask, output, values.Intensity, preserveAlpha: false);
                return graph.Output(output);
            });
    }

    private static readonly ShaderProgram s_rays = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0) uniform sampler2D image;
        layout(binding = 1, std140) uniform Source { vec4 logical; vec4 raster; vec4 size; } src;
        layout(binding = 2, std140) uniform Destination { vec4 logical; vec4 raster; vec4 size; } dst;
        layout(push_constant) uniform Values { vec2 center; float inverseStretch; float decay; } pc;
        layout(location = 0) out vec4 color;
        void main() {
            vec2 position = dst.raster.xy + gl_FragCoord.xy / dst.size.z;
            vec2 end = pc.center + (position - pc.center) * pc.inverseStretch;
            vec2 delta = end - position;
            float first = 0;
            float last = 1;
            color = vec4(0);
            // Clip to the actual source before sampling. Distant centers do not add empty samples,
            // and the support stays inside the forward-transformed source bounds.
            for (int axis = 0; axis < 2; axis++) {
                float low = src.logical[axis];
                float high = low + src.logical[axis + 2];
                if (delta[axis] == 0) {
                    if (position[axis] < low || position[axis] >= high) return;
                } else {
                    float a = (low - position[axis]) / delta[axis];
                    float b = (high - position[axis]) / delta[axis];
                    first = max(first, min(a, b));
                    last = min(last, max(a, b));
                }
            }
            if (last <= first) return;
            float span = last - first;
            vec2 pixels = abs(delta) * span * src.size.z;
            // At most one texel between samples keeps narrow highlights continuous at every scale.
            // A small minimum also integrates the decay smoothly near the center.
            int steps = max(16, int(ceil(max(pixels.x, pixels.y))));
            for (int i = 0; i < steps; i++) {
                float t = first + (float(i) + 0.5) * span / float(steps);
                vec2 p = position + delta * t;
                float weight = pc.decay > 0 ? pow(max(1 - t, 0), pc.decay) : 1;
                // Loop counts vary by pixel, so avoid implicit texture derivatives in this control flow.
                color += textureLod(image, (p - src.raster.xy) / src.raster.zw, 0) * weight;
            }
            // Normalize over the complete ray, including transparent regions outside the source.
            color *= span / float(steps);
        }
        """));
}

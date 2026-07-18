using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Threshold), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Threshold : FilterEffect
{
    // Fusable snippet form of the shader above; `c` is the premultiplied linear-light source pixel (contract A2).
    // The original reads c.rgb directly (no unpremultiply) and returns half4(t); preserved exactly.
    private static readonly string s_snippet =
        """
        uniform float threshold;
        uniform float smoothness;
        uniform float strength;

        const float3 LUMA = float3(0.2126, 0.7152, 0.0722);

        half4 apply(half4 c) {
            float3 rgb = c.rgb;

            float luma = dot(rgb, LUMA);
            float lower = threshold - smoothness * 0.5;
            float upper = threshold + smoothness * 0.5;
            float t = smoothstep(lower, upper, luma);

            t = mix(luma, t, strength);
            return half4(t);
        }
        """;

    public Threshold()
    {
        ScanProperties<Threshold>();
    }

    [Display(Name = nameof(GraphicsStrings.Amount), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IProperty<float> Value { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(GraphicsStrings.Smoothing), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IProperty<float> Smoothness { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Strength), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IProperty<float> Strength { get; } = Property.CreateAnimatable(100f);

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        builder.Shader(ShaderNodeDescriptor.Snippet(
            s_snippet,
            u => u.Float("threshold", r.Value / 100f)
                .Float("smoothness", r.Smoothness / 100f)
                .Float("strength", r.Strength / 100f)));
    }
}

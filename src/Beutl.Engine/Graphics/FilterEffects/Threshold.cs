using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Threshold), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Threshold : FilterEffect
{
    private const string ShaderSource =
        """
        uniform float threshold;
        uniform float smoothness;
        uniform float strength;

        const float3 LUMA = float3(0.2126, 0.7152, 0.0722);

        half4 apply(half4 color) {
            float3 rgb = color.rgb;

            float luma = dot(rgb, LUMA);
            float lower = threshold - smoothness * 0.5;
            float upper = threshold + smoothness * 0.5;
            float t = smoothstep(lower, upper, luma);

            t = mix(luma, t, strength);
            return half4(t);
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.CurrentPixel);

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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.Shader(ShaderDescription.CurrentPixel(
            s_shaderSource,
            bindings =>
            {
                bindings.Uniform("threshold", r.Value / 100f);
                bindings.Uniform("smoothness", r.Smoothness / 100f);
                bindings.Uniform("strength", r.Strength / 100f);
            }));
    }
}

using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
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

            // smoothstep over a zero-width band differs by backend (Skia's CPU returns NaN, Metal 0), so the
            // collapsed band is written out: 0.5 at the threshold, as any positive width gives, and a hard step.
            float t = upper > lower
                ? smoothstep(lower, upper, luma)
                : (luma < threshold ? 0.0 : (luma > threshold ? 1.0 : 0.5));

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
            },
            CreatesAlphaFromATransparentPixel(r) ? RenderHitTestContract.OutputBounds : null));
    }

    /// <remarks>
    /// A setting that gives a transparent pixel (luma 0) a positive <c>t</c> makes the hit test the output
    /// rectangle. The check evaluates the shader expression itself.
    /// </remarks>
    private static bool CreatesAlphaFromATransparentPixel(Resource r)
    {
        const float luma = 0f;
        float threshold = r.Value / 100f;
        float smoothness = r.Smoothness / 100f;
        float lower = threshold - (smoothness * 0.5f);
        float upper = threshold + (smoothness * 0.5f);
        float t;
        if (upper > lower)
        {
            float x = Math.Clamp((luma - lower) / (upper - lower), 0f, 1f);
            t = x * x * (3f - (2f * x));
        }
        else
        {
            t = luma < threshold ? 0f : luma > threshold ? 1f : 0.5f;
        }

        return !(t * (r.Strength / 100f) <= 0f);
    }
}

using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Graphics.Rendering;
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
            },
            CreatesAlphaFromATransparentPixel(r) ? RenderHitTestContract.OutputBounds : null));
    }

    /// <remarks>
    /// The entry point returns <c>half4(t)</c>, so <c>t</c> is the output alpha as well as the colour: at the
    /// settings where a fully transparent pixel leaves with a non-zero <c>t</c>, the stage paints where its
    /// input covers nothing, and a hit test forwarded to that input would miss pixels the viewer can see.
    /// This evaluates the entry point itself at the luma a transparent premultiplied pixel carries - zero -
    /// rather than restating it as an inequality on the properties: the answer turns over at exactly the
    /// parameter boundaries a restatement gets wrong, and it has to keep tracking the SkSL above.
    /// </remarks>
    private static bool CreatesAlphaFromATransparentPixel(Resource r)
    {
        float threshold = r.Value / 100f;
        float smoothness = r.Smoothness / 100f;
        float lower = threshold - (smoothness * 0.5f);
        float upper = threshold + (smoothness * 0.5f);
        float x = (0f - lower) / (upper - lower);
        // Written out rather than clamped so a degenerate lower == upper keeps the NaN the shader produces.
        float t = x < 0f ? 0f : x > 1f ? 1f : x;
        t = t * t * (3f - (2f * t));
        return !(t * (r.Strength / 100f) <= 0f);
    }
}

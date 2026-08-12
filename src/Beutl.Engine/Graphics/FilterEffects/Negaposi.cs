using System.ComponentModel.DataAnnotations;
using System.Numerics;

using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Negaposi), ResourceType = typeof(GraphicsStrings))]
public partial class Negaposi : FilterEffect
{
    private const string ShaderSource =
        """
        uniform float3 negaColor;
        uniform float strength;

        half4 apply(half4 color) {
            float alpha = color.a;
            if (alpha <= 0.0001) return half4(0.0);
            float3 rgb = color.rgb / alpha;

            float3 negated = negaColor - rgb;
            float3 result = mix(rgb, negated, strength);

            return half4(half3(result * alpha), half(alpha));
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.CurrentPixel);

    public Negaposi()
    {
        ScanProperties<Negaposi>();
    }

    [Display(Name = nameof(GraphicsStrings.Negaposi_Red), ResourceType = typeof(GraphicsStrings))]
    public IProperty<int> Red { get; } = Property.CreateAnimatable<int>();

    [Display(Name = nameof(GraphicsStrings.Negaposi_Green), ResourceType = typeof(GraphicsStrings))]
    public IProperty<int> Green { get; } = Property.CreateAnimatable<int>();

    [Display(Name = nameof(GraphicsStrings.Negaposi_Blue), ResourceType = typeof(GraphicsStrings))]
    public IProperty<int> Blue { get; } = Property.CreateAnimatable<int>();

    [Range(0, 100)]
    [Display(Name = nameof(GraphicsStrings.Strength), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Strength { get; } = Property.CreateAnimatable(100f);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.Shader(ShaderDescription.CurrentPixel(
            s_shaderSource,
            bindings =>
            {
                bindings.Uniform(
                    "negaColor",
                    new Vector3(
                        Color.SrgbToLinear(r.Red / 255f),
                        Color.SrgbToLinear(r.Green / 255f),
                        Color.SrgbToLinear(r.Blue / 255f)));
                bindings.Uniform("strength", r.Strength / 100f);
            }));
    }
}

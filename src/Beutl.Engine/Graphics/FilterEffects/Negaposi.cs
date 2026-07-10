using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Negaposi), ResourceType = typeof(GraphicsStrings))]
public partial class Negaposi : FilterEffect
{
    // Fusable snippet form of the shader above; `c` is the premultiplied linear-light source pixel (contract A2).
    private static readonly string s_snippet =
        """
        uniform float3 negaColor;
        uniform float strength;

        half4 apply(half4 c) {
            float alpha = c.a;
            if (alpha <= 0.0001) return half4(0.0);
            float3 rgb = c.rgb / alpha;

            float3 negated = negaColor - rgb;
            float3 result = mix(rgb, negated, strength);

            return half4(half3(result * alpha), half(alpha));
        }
        """;

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

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        float negR = Color.SrgbToLinear(r.Red / 255f);
        float negG = Color.SrgbToLinear(r.Green / 255f);
        float negB = Color.SrgbToLinear(r.Blue / 255f);
        builder.Shader(ShaderNodeDescriptor.Snippet(
            s_snippet,
            u => u.Float3("negaColor", negR, negG, negB).Float("strength", r.Strength / 100f)));
    }
}

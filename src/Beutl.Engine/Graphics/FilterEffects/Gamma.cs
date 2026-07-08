using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Gamma), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Gamma : FilterEffect
{
    // Fusable snippet; `c` is the premultiplied linear-light source pixel (contract A2).
    private static readonly string s_snippet =
        """
        uniform float gamma;
        uniform float strength;

        half4 apply(half4 c) {
            float alpha = c.a;
            if (alpha <= 0.0001) return half4(0.0);
            float3 rgb = c.rgb / alpha;

            float3 corrected = pow(max(rgb, float3(0.0)), float3(1.0 / gamma));
            float3 result = mix(rgb, corrected, strength);

            return half4(half3(result * alpha), half(alpha));
        }
        """;

    public Gamma()
    {
        ScanProperties<Gamma>();
    }

    [Display(Name = nameof(GraphicsStrings.Amount), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 300)]
    public IProperty<float> Amount { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Strength), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IProperty<float> Strength { get; } = Property.CreateAnimatable(100f);

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        float gamma = Math.Clamp(r.Amount / 100f, 0.01f, 3f);
        float strength = r.Strength / 100f;
        builder.Shader(ShaderNodeDescriptor.Snippet(
            s_snippet, u => u.Float("gamma", gamma).Float("strength", strength)));
    }
}

using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Invert), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Invert : FilterEffect
{
    private const string ShaderSource =
        """
        uniform float amount;
        uniform int excludeAlpha;

        half4 apply(half4 color) {
            float alpha = color.a;
            if (alpha <= 0.0001) return half4(0.0);
            float3 rgb = color.rgb / alpha;

            float3 inverted = 1.0 - rgb;
            float3 result = mix(rgb, inverted, amount);

            if (excludeAlpha == 0) {
                float newAlpha = mix(alpha, 1.0 - alpha, amount);
                return half4(half3(result * newAlpha), half(newAlpha));
            }
            return half4(half3(result * alpha), half(alpha));
        }
        """;

    public Invert()
    {
        ScanProperties<Invert>();
    }

    [Range(0, 100)]
    [Display(Name = nameof(GraphicsStrings.Amount), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Amount { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Invert_ExcludeAlphaChannel), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> ExcludeAlphaChannel { get; } = Property.CreateAnimatable(true);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.Shader(ShaderDescription.CurrentPixel(
            ShaderSource,
            bindings =>
            {
                bindings.Uniform("amount", r.Amount / 100f);
                bindings.Uniform("excludeAlpha", r.ExcludeAlphaChannel ? 1 : 0);
            }));
    }
}

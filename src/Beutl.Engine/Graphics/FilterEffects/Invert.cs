using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Invert), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Invert : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<Invert>();
    private static readonly SKSLShader? s_shader;

    static Invert()
    {
        string sksl =
            """
            uniform shader src;
            uniform float amount;
            uniform int excludeAlpha;

            half4 main(float2 coord) {
                half4 c = src.eval(coord);
                float alpha = c.a;
                if (alpha <= 0.0001) return half4(0.0);
                float3 rgb = c.rgb / alpha;

                float3 inverted = 1.0 - rgb;
                float3 result = mix(rgb, inverted, amount);

                if (excludeAlpha == 0) {
                    float newAlpha = mix(alpha, 1.0 - alpha, amount);
                    return half4(half3(result * newAlpha), half(newAlpha));
                }
                return half4(half3(result * alpha), half(alpha));
            }
            """;

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile invert shader: {ErrorText}", errorText);
        }
    }

    // Fusable snippet form of the shader above; `c` is the premultiplied linear-light source pixel (contract A2).
    private static readonly string s_snippet =
        """
        uniform float amount;
        uniform int excludeAlpha;

        half4 apply(half4 c) {
            float alpha = c.a;
            if (alpha <= 0.0001) return half4(0.0);
            float3 rgb = c.rgb / alpha;

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

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        builder.Shader(ShaderNodeDescriptor.Snippet(
            s_snippet,
            u => u.Float("amount", r.Amount / 100f).Int("excludeAlpha", r.ExcludeAlphaChannel ? 1 : 0)));
    }
}

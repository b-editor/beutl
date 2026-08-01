using System.ComponentModel.DataAnnotations;
using System.Reactive;

using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

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
        if (s_shader is null)
        {
            throw new InvalidOperationException("Failed to compile SKSL.");
        }

        var r = (Resource)resource;
        context.CustomEffect(
            (r, Unit.Default),
            (t, c) => OnApply(t.r, c),
            static (_, rect) => rect);
    }

    private static void OnApply(Resource data, CustomFilterEffectContext context)
    {
        if (s_shader is null) return;

        for (int i = 0; i < context.Targets.Count; i++)
        {
            using var target = context.Targets[i];
            var renderTarget = target.RenderTarget!;

            using SKImage image = renderTarget.Value.Snapshot();
            using SKShader baseShader = image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal);
            var builder = s_shader.CreateBuilder();

            builder.Children["src"] = baseShader;
            builder.Uniforms["amount"] = data.Amount / 100f;
            builder.Uniforms["excludeAlpha"] = data.ExcludeAlphaChannel ? 1 : 0;

            context.Targets[i] = s_shader.ApplyToNewTarget(context, builder, target.Bounds);
        }
    }
}

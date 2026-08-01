using System.ComponentModel.DataAnnotations;
using System.Reactive;

using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Gamma), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Gamma : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<Gamma>();
    private static readonly SKSLShader? s_shader;

    static Gamma()
    {
        string sksl =
            """
            uniform shader src;
            uniform float gamma;
            uniform float strength;

            half4 main(float2 coord) {
                half4 c = src.eval(coord);
                float alpha = c.a;
                if (alpha <= 0.0001) return half4(0.0);
                float3 rgb = c.rgb / alpha;

                float3 corrected = pow(max(rgb, float3(0.0)), float3(1.0 / gamma));
                float3 result = mix(rgb, corrected, strength);

                return half4(half3(result * alpha), half(alpha));
            }
            """;

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile gamma shader: {ErrorText}", errorText);
        }
    }

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

        float gamma = Math.Clamp(data.Amount / 100f, 0.01f, 3f);
        float strength = data.Strength / 100f;

        for (int i = 0; i < context.Targets.Count; i++)
        {
            using var target = context.Targets[i];
            var renderTarget = target.RenderTarget!;

            using SKImage image = renderTarget.Value.Snapshot();
            using SKShader baseShader = image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal);
            var builder = s_shader.CreateBuilder();

            builder.Children["src"] = baseShader;
            builder.Uniforms["gamma"] = gamma;
            builder.Uniforms["strength"] = strength;

            context.Targets[i] = s_shader.ApplyToNewTarget(context, builder, target.Bounds);
        }
    }
}

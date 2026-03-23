using System.ComponentModel.DataAnnotations;
using System.Reactive;

using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Threshold), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Threshold : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<Threshold>();
    private static readonly SKSLShader? s_shader;

    static Threshold()
    {
        string sksl =
            """
            uniform shader src;
            uniform float threshold;
            uniform float smoothness;
            uniform float strength;

            const float3 LUMA = float3(0.2126, 0.7152, 0.0722);

            half4 main(float2 coord) {
                half4 c = src.eval(coord);
                float3 rgb = c.rgb;

                float luma = dot(rgb, LUMA);
                float lower = threshold - smoothness * 0.5;
                float upper = threshold + smoothness * 0.5;
                float t = smoothstep(lower, upper, luma);

                t = mix(luma, t, strength);
                return half4(t);
            }
            """;

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile threshold shader: {ErrorText}", errorText);
        }
    }

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
            builder.Uniforms["threshold"] = data.Value / 100f;
            builder.Uniforms["smoothness"] = data.Smoothness / 100f;
            builder.Uniforms["strength"] = data.Strength / 100f;

            context.Targets[i] = s_shader.ApplyToNewTarget(context, builder, target.Bounds);
        }
    }
}

using System.ComponentModel.DataAnnotations;
using System.Reactive;

using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Negaposi), ResourceType = typeof(GraphicsStrings))]
public partial class Negaposi : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<Negaposi>();
    private static readonly SKSLShader? s_shader;

    static Negaposi()
    {
        string sksl =
            """
            uniform shader src;
            uniform float3 negaColor;
            uniform float strength;

            half4 main(float2 coord) {
                half4 c = src.eval(coord);
                float alpha = c.a;
                if (alpha <= 0.0001) return half4(0.0);
                float3 rgb = c.rgb / alpha;

                float3 negated = negaColor - rgb;
                float3 result = mix(rgb, negated, strength);

                return half4(half3(result * alpha), half(alpha));
            }
            """;

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile negaposi shader: {ErrorText}", errorText);
        }
    }

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

    private static void OnApply(Resource data, CustomFilterEffectContext context)
    {
        if (s_shader is null) return;

        float negR = Color.SrgbToLinear(data.Red / 255f);
        float negG = Color.SrgbToLinear(data.Green / 255f);
        float negB = Color.SrgbToLinear(data.Blue / 255f);
        float strength = data.Strength / 100f;

        for (int i = 0; i < context.Targets.Count; i++)
        {
            using var target = context.Targets[i];
            var renderTarget = target.RenderTarget!;

            using SKImage image = renderTarget.Value.Snapshot();
            using SKShader baseShader = image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal);
            var builder = s_shader.CreateBuilder();

            builder.Children["src"] = baseShader;
            builder.Uniforms["negaColor"] = new SKColorF(negR, negG, negB);
            builder.Uniforms["strength"] = strength;

            context.Targets[i] = s_shader.ApplyToNewTarget(context, builder, target.Bounds);
        }
    }
}

using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.ColorKey), ResourceType = typeof(Strings))]
public partial class ColorKey : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<ColorKey>();
    private static readonly SKSLShader? s_shader;

    static ColorKey()
    {
        string sksl =
            """
            uniform shader src;
            uniform float4 color;
            uniform float range;
            uniform float boundary;

            // Rec.601 での輝度変換
            half calcLuma(half3 c) {
                return clamp(dot(c, half3(0.299, 0.587, 0.114)), 0.0, 1.0);
            }

            half4 main(float2 fragCoord) {
                // 入力画像から色を取得
                half4 c = src.eval(fragCoord);

                // 入力色と基準色を輝度に変換
                half luma = calcLuma(c.rgb);
                half keyLuma = calcLuma(color.rgb);

                half diff = abs(luma - keyLuma);

                half mask = smoothstep(range, range + boundary, diff);

                return c * mask;
            }
            """;

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

    public ColorKey()
    {
        ScanProperties<ColorKey>();
    }

    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public IProperty<Color> Color { get; } = Property.CreateAnimatable<Color>();

    [Display(Name = nameof(Strings.BrightnessRange), ResourceType = typeof(Strings))]
    public IProperty<float> Range { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.BoundaryCorrection), ResourceType = typeof(Strings))]
    public IProperty<float> Boundary { get; } = Property.CreateAnimatable(2f);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.CustomEffect(
            (r.Color, r.Range, r.Boundary),
            OnApplyTo,
            static (_, r) => r);
    }

    private static void OnApplyTo((Color color, float range, float boundary) data, CustomFilterEffectContext c)
    {
        if (s_shader is null) return;

        for (int i = 0; i < c.Targets.Count; i++)
        {
            using EffectTarget effectTarget = c.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = image.ToShader();

            // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
            var builder = s_shader.CreateBuilder();

            // child shaderとしてテクスチャ用のシェーダーを設定
            builder.Children["src"] = baseShader;
            builder.Uniforms["color"] = new SKColor(data.color.R, data.color.G, data.color.B, data.color.A);
            builder.Uniforms["range"] = data.range / 100f;
            builder.Uniforms["boundary"] = data.boundary / 100f;

            // 新しいターゲットに適用
            c.Targets[i] = s_shader.ApplyToNewTarget(c, builder, effectTarget.Bounds);
        }
    }
}

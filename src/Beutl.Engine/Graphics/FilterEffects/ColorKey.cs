using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.ColorKey), ResourceType = typeof(GraphicsStrings))]
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

            // Rec.709 での輝度変換（リニアsRGB用）
            half calcLuma(half3 c) {
                return clamp(dot(c, half3(0.2126, 0.7152, 0.0722)), 0.0, 1.0);
            }

            half4 main(float2 fragCoord) {
                half4 c = src.eval(fragCoord);

                // プリマルチプライドアルファを解除
                half alpha = c.a;
                if (alpha <= 0.0001) return half4(0.0);
                half3 rgb = c.rgb / alpha;

                // リニア空間でRec.709係数を使って輝度を計算
                half luma = calcLuma(rgb);
                half keyLuma = calcLuma(color.rgb);

                half diff = abs(luma - keyLuma);
                half mask = smoothstep(range, range + boundary, diff);

                // 元のプリマルチプライド値にマスクを乗算して透過させる
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

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Color> Color { get; } = Property.CreateAnimatable<Color>();

    [Display(Name = nameof(GraphicsStrings.ColorKey_Range), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Range { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ColorKey_Boundary), ResourceType = typeof(GraphicsStrings))]
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

            var builder = s_shader.CreateBuilder();

            builder.Children["src"] = baseShader;
            builder.Uniforms["color"] = data.color.ToLinear().ToSKColorF();
            builder.Uniforms["range"] = data.range / 100f;
            builder.Uniforms["boundary"] = data.boundary / 100f;

            c.Targets[i] = s_shader.ApplyToNewTarget(c, builder, effectTarget.Bounds);
        }
    }
}

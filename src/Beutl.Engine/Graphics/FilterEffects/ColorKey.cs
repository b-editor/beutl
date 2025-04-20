using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class ColorKey : FilterEffect
{
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<float> RangeProperty;
    private static readonly CoreProperty<float> BoundaryProperty;
    private static readonly ILogger s_logger = Log.CreateLogger<ColorKey>();
    private static readonly SKRuntimeEffect? s_runtimeEffect;
    private Color _color;
    private float _range;
    private float _boundary = 2f;

    static ColorKey()
    {
        ColorProperty = ConfigureProperty<Color, ColorKey>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .Register();

        RangeProperty = ConfigureProperty<float, ColorKey>(nameof(Range))
            .Accessor(o => o.Range, (o, v) => o.Range = v)
            .Register();

        BoundaryProperty = ConfigureProperty<float, ColorKey>(nameof(Boundary))
            .Accessor(o => o.Boundary, (o, v) => o.Boundary = v)
            .DefaultValue(2f)
            .Register();

        AffectsRender<ColorKey>(ColorProperty, RangeProperty, BoundaryProperty);
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

        // SKRuntimeEffectを使ってSKSLコードをコンパイル
        s_runtimeEffect = SKRuntimeEffect.CreateShader(sksl, out string? errorText);
        if (errorText is not null)
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public Color Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, value);
    }

    [Display(Name = nameof(Strings.BrightnessRange), ResourceType = typeof(Strings))]
    public float Range
    {
        get => _range;
        set => SetAndRaise(RangeProperty, ref _range, value);
    }

    [Display(Name = nameof(Strings.BoundaryCorrection), ResourceType = typeof(Strings))]
    public float Boundary
    {
        get => _boundary;
        set => SetAndRaise(BoundaryProperty, ref _boundary, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((Color, Range, Boundary), OnApplyTo, (_, r) => r);
    }

    private static void OnApplyTo((Color color, float range, float boundary) data, CustomFilterEffectContext c)
    {
        for (int i = 0; i < c.Targets.Count; i++)
        {
            EffectTarget effectTarget = c.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = SKShader.CreateImage(image);

            // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
            var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);

            // child shaderとしてテクスチャ用のシェーダーを設定
            builder.Children["src"] = baseShader;
            builder.Uniforms["color"] = new SKColor(data.color.R, data.color.G, data.color.B, data.color.A);
            builder.Uniforms["range"] = data.range / 100f;
            builder.Uniforms["boundary"] = data.boundary / 100f;

            // 最終的なシェーダーを生成
            using (SKShader finalShader = builder.Build())
            using (var paint = new SKPaint())
            {
                var newTarget = c.CreateTarget(effectTarget.Bounds);
                var canvas = newTarget.RenderTarget!.Value.Canvas;
                paint.Shader = finalShader;
                canvas.DrawRect(new SKRect(0, 0, effectTarget.Bounds.Width, effectTarget.Bounds.Height), paint);

                c.Targets[i] = newTarget;
            }

            effectTarget.Dispose();
        }
    }
}

using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class ChromaKey : FilterEffect
{
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<float> HueRangeProperty;
    public static readonly CoreProperty<float> SaturationRangeProperty;
    private static readonly ILogger s_logger = Log.CreateLogger<ChromaKey>();
    private static readonly SKRuntimeEffect? s_runtimeEffect;
    private Color _color;
    private float _hueRange;
    private float _saturationRange;

    static ChromaKey()
    {
        ColorProperty = ConfigureProperty<Color, ChromaKey>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .Register();

        HueRangeProperty = ConfigureProperty<float, ChromaKey>(nameof(HueRange))
            .Accessor(o => o.HueRange, (o, v) => o.HueRange = v)
            .Register();

        SaturationRangeProperty = ConfigureProperty<float, ChromaKey>(nameof(SaturationRange))
            .Accessor(o => o.SaturationRange, (o, v) => o.SaturationRange = v)
            .Register();

        AffectsRender<ChromaKey>(ColorProperty, HueRangeProperty, SaturationRangeProperty);
        string sksl =
            """
            uniform shader src;
            uniform float4 color;
            uniform float hueRange;
            uniform float saturationRange;

            // RGBからHSVへの変換関数
            half3 rgb2hsv(half3 c) {
                half r = c.r;
                half g = c.g;
                half b = c.b;
                half maxc = max(r, max(g, b));
                half minc = min(r, min(g, b));
                half delta = maxc - minc;
                half h = 0.0;

                if (delta > 0.00001) {
                    if (maxc == r) {
                        h = mod((g - b) / delta, 6.0);
                    } else if (maxc == g) {
                        h = (b - r) / delta + 2.0;
                    } else {
                        h = (r - g) / delta + 4.0;
                    }
                    h = h / 6.0; // 0～1の範囲に正規化
                }

                half s = (maxc <= 0.0) ? 0.0 : (delta / maxc);
                half v = maxc;
                return half3(h, s, v);
            }

            half4 main(float2 fragCoord) {
                // 入力画像から色を取得
                half4 c = src.eval(fragCoord);

                // 入力色と基準色をHSVに変換
                half3 hsv = rgb2hsv(c.rgb);
                half3 refHsv = rgb2hsv(color.rgb);

                // 色相の差を計算（周期性を考慮）
                half hueDiff = abs(hsv.x - refHsv.x);
                hueDiff = min(hueDiff, 1.0 - hueDiff);

                // 色相と彩度が指定範囲内ならアルファ値を0（透明）に
                if (hueDiff < hueRange && abs(hsv.y - refHsv.y) < saturationRange) {
                    return half4(c.r, c.g, c.b, 0.0);
                }

                return c;
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

    [Display(Name = nameof(Strings.HueRange), ResourceType = typeof(Strings))]
    public float HueRange
    {
        get => _hueRange;
        set => SetAndRaise(HueRangeProperty, ref _hueRange, value);
    }

    [Display(Name = nameof(Strings.SaturationRange), ResourceType = typeof(Strings))]
    public float SaturationRange
    {
        get => _saturationRange;
        set => SetAndRaise(SaturationRangeProperty, ref _saturationRange, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((Color, HueRange, SaturationRange), OnApplyTo, (_, r) => r);
    }

    private static void OnApplyTo((Color color, float hueRange, float satRange) data, CustomFilterEffectContext c)
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
            builder.Uniforms["hueRange"] = data.hueRange / 360f;
            builder.Uniforms["saturationRange"] = data.satRange / 100f;

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

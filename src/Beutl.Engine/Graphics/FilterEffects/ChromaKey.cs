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
    public static readonly CoreProperty<float> BoundaryProperty;
    private static readonly ILogger s_logger = Log.CreateLogger<ChromaKey>();
    private static readonly SKRuntimeEffect? s_runtimeEffect;
    private Color _color;
    private float _hueRange;
    private float _saturationRange;
    private float _boundary = 2f;

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

        BoundaryProperty = ConfigureProperty<float, ChromaKey>(nameof(Boundary))
            .Accessor(o => o.Boundary, (o, v) => o.Boundary = v)
            .DefaultValue(2f)
            .Register();

        AffectsRender<ChromaKey>(ColorProperty, HueRangeProperty, SaturationRangeProperty, BoundaryProperty);
        string sksl =
            """
            uniform shader src;
            uniform float4 color;
            uniform float hueRange;
            uniform float saturationRange;
            uniform float boundary;

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
                half3 keyHSV = rgb2hsv(color.rgb);

                // 色相の差を計算（周期性を考慮）
                half hueDiff = abs(hsv.x - keyHSV.x);
                hueDiff = min(hueDiff, 1.0 - hueDiff);

                // 彩度の差の絶対値
                half satDiff = abs(hsv.y - keyHSV.y);

                half maskHue = smoothstep(hueRange, hueRange + boundary, hueDiff);
                half maskSat = smoothstep(saturationRange, saturationRange + boundary, satDiff);

                // 色相と彩度の両条件を満たすかを判定（両方のマスク値が低いほど基準色に近いと判断）
                half mask = max(maskHue, maskSat);

                // 出力色のアルファ値にマスクを乗算して、クロマキー部分を透過させる
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

    [Display(Name = nameof(Strings.BoundaryCorrection), ResourceType = typeof(Strings))]
    public float Boundary
    {
        get => _boundary;
        set => SetAndRaise(BoundaryProperty, ref _boundary, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((Color, HueRange, SaturationRange, Boundary), OnApplyTo, (_, r) => r);
    }

    private static void OnApplyTo((Color color, float hueRange, float satRange, float boundary) data, CustomFilterEffectContext c)
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

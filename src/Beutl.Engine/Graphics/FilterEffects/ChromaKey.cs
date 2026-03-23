using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.ChromaKey), ResourceType = typeof(GraphicsStrings))]
public partial class ChromaKey : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<ChromaKey>();
    private static readonly SKSLShader? s_shader;

    static ChromaKey()
    {
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

            // リニアsRGB -> sRGBガンマ変換
            half3 linearToSrgb(half3 c) {
                half3 lo = c * 12.92;
                half3 hi = 1.055 * pow(c, half3(1.0/2.4)) - 0.055;
                return mix(lo, hi, step(half3(0.0031308), c));
            }

            half4 main(float2 fragCoord) {
                half4 c = src.eval(fragCoord);

                // プリマルチプライドアルファを解除
                half alpha = c.a;
                if (alpha <= 0.0001) return half4(0.0);
                half3 rgb = c.rgb / alpha;

                // リニアsRGB → sRGBガンマに変換してからHSV比較
                // （color uniformはsRGBガンマ空間のため、同じ空間で比較する）
                half3 srgbColor = linearToSrgb(rgb);

                half3 hsv = rgb2hsv(srgbColor);
                half3 keyHSV = rgb2hsv(color.rgb);

                // 色相の差を計算（周期性を考慮）
                half hueDiff = abs(hsv.x - keyHSV.x);
                hueDiff = min(hueDiff, 1.0 - hueDiff);

                // 彩度の差の絶対値
                half satDiff = abs(hsv.y - keyHSV.y);

                half maskHue = smoothstep(hueRange, hueRange + boundary, hueDiff);
                half maskSat = smoothstep(saturationRange, saturationRange + boundary, satDiff);

                // 色相と彩度の両条件を満たすかを判定
                half mask = max(maskHue, maskSat);

                // 元のプリマルチプライド値にマスクを乗算して透過させる
                return c * mask;
            }
            """;

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

    public ChromaKey()
    {
        ScanProperties<ChromaKey>();
    }

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Color> Color { get; } = Property.CreateAnimatable<Color>();

    [Display(Name = nameof(GraphicsStrings.ChromaKey_HueRange), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> HueRange { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ChromaKey_SaturationRange), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> SaturationRange { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ChromaKey_Boundary), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Boundary { get; } = Property.CreateAnimatable(2f);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.CustomEffect(
            (color: r.Color, hueRange: r.HueRange, satRange: r.SaturationRange, boundary: r.Boundary),
            OnApplyTo,
            static (_, r) => r);
    }

    private static void OnApplyTo((Color color, float hueRange, float satRange, float boundary) data, CustomFilterEffectContext c)
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
            builder.Uniforms["color"] = data.color.ToSKColor();
            builder.Uniforms["hueRange"] = data.hueRange / 360f;
            builder.Uniforms["saturationRange"] = data.satRange / 100f;
            builder.Uniforms["boundary"] = data.boundary / 100f;

            c.Targets[i] = s_shader.ApplyToNewTarget(c, builder, effectTarget.Bounds);
        }
    }
}

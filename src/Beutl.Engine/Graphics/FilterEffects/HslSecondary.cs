using System.ComponentModel.DataAnnotations;
using System.Reactive;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.HslSecondary), ResourceType = typeof(GraphicsStrings))]
public sealed partial class HslSecondary : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<HslSecondary>();
    private static readonly SKSLShader? s_shader;

    static HslSecondary()
    {
        const string sksl =
            """
            uniform shader src;
            uniform float hueCenter;
            uniform float hueRange;
            uniform float hueFeather;
            uniform float satMin;
            uniform float satMax;
            uniform float satFeather;
            uniform float lumMin;
            uniform float lumMax;
            uniform float lumFeather;
            uniform float hueShift;
            uniform float satAdjust;
            uniform float lumAdjust;
            uniform float showMask;

            float3 rgb_to_hsl(float3 c) {
                float maxc = max(max(c.r, c.g), c.b);
                float minc = min(min(c.r, c.g), c.b);
                float l = (maxc + minc) * 0.5;
                float h = 0.0;
                float s = 0.0;
                float d = maxc - minc;
                if (d > 1e-6) {
                    s = (l > 0.5) ? d / max(2.0 - maxc - minc, 1e-6) : d / max(maxc + minc, 1e-6);
                    if (maxc == c.r) {
                        h = (c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0);
                    } else if (maxc == c.g) {
                        h = (c.b - c.r) / d + 2.0;
                    } else {
                        h = (c.r - c.g) / d + 4.0;
                    }
                    h /= 6.0;
                }
                return float3(h, s, l);
            }

            float hue_to_rgb(float p, float q, float t) {
                if (t < 0.0) t += 1.0;
                if (t > 1.0) t -= 1.0;
                if (t < 1.0/6.0) return p + (q - p) * 6.0 * t;
                if (t < 1.0/2.0) return q;
                if (t < 2.0/3.0) return p + (q - p) * (2.0/3.0 - t) * 6.0;
                return p;
            }

            float3 hsl_to_rgb(float3 hsl) {
                float h = hsl.x;
                float s = hsl.y;
                float l = hsl.z;
                if (s <= 0.0) return float3(l);
                float q = (l < 0.5) ? l * (1.0 + s) : l + s - l * s;
                float p = 2.0 * l - q;
                return float3(
                    hue_to_rgb(p, q, h + 1.0/3.0),
                    hue_to_rgb(p, q, h),
                    hue_to_rgb(p, q, h - 1.0/3.0)
                );
            }

            float linearToSrgbScalar(float c) {
                return (c <= 0.0031308) ? c * 12.92 : 1.055 * pow(c, 1.0/2.4) - 0.055;
            }

            float srgbToLinearScalar(float c) {
                return (c <= 0.04045) ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
            }

            float3 linearToSrgb(float3 c) {
                return float3(linearToSrgbScalar(c.r), linearToSrgbScalar(c.g), linearToSrgbScalar(c.b));
            }

            float3 srgbToLinear(float3 c) {
                return float3(srgbToLinearScalar(c.r), srgbToLinearScalar(c.g), srgbToLinearScalar(c.b));
            }

            float hue_distance(float a, float b) {
                float d = abs(a - b);
                return min(d, 1.0 - d);
            }

            float range_mask(float v, float lo, float hi, float feather) {
                float a = smoothstep(lo - feather, lo, v);
                float b = 1.0 - smoothstep(hi, hi + feather, v);
                return clamp(a * b, 0.0, 1.0);
            }

            half4 main(float2 coord) {
                half4 baseColor = src.eval(coord);
                if (baseColor.a <= 0.0001) return baseColor;

                // 全調整がゼロかつマスク表示でない場合は HSL ラウンドトリップを回避し、
                // 上流の HDR/範囲外リニア RGB をそのまま通す。
                if (showMask <= 0.5 && hueShift == 0.0 && satAdjust == 0.0 && lumAdjust == 0.0) {
                    return baseColor;
                }

                float3 rgb = linearToSrgb(clamp(baseColor.rgb / baseColor.a, 0.0, 1.0));
                float3 hsl = rgb_to_hsl(rgb);

                // Hue mask with circular feather (hue is normalized 0..1)
                float hueHalf = hueRange * 0.5;
                float hd = hue_distance(hsl.x, hueCenter);
                float hueMask = 1.0 - smoothstep(hueHalf, hueHalf + max(hueFeather, 1e-5), hd);

                float satMask = range_mask(hsl.y, satMin, satMax, max(satFeather, 1e-5));
                float lumMask = range_mask(hsl.z, lumMin, lumMax, max(lumFeather, 1e-5));

                float mask = clamp(hueMask * satMask * lumMask, 0.0, 1.0);

                if (showMask > 0.5) {
                    float3 m = float3(mask);
                    float3 lin = srgbToLinear(m);
                    return half4(half3(lin * baseColor.a), baseColor.a);
                }

                // マスク外は元の色を保持し、HDR/範囲外値のクリップを防ぐ。
                if (mask <= 0.0) return baseColor;

                // Apply adjustments scaled by mask
                hsl.x = fract(hsl.x + hueShift * mask + 1.0);
                hsl.y = clamp(hsl.y + satAdjust * mask, 0.0, 1.0);
                hsl.z = clamp(hsl.z + lumAdjust * mask, 0.0, 1.0);

                float3 outRgb = hsl_to_rgb(hsl);
                float3 result = srgbToLinear(outRgb);
                return half4(half3(result * baseColor.a), baseColor.a);
            }
            """;

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile HSL secondary shader: {ErrorText}", errorText);
        }
    }

    public HslSecondary()
    {
        ScanProperties<HslSecondary>();
    }

    [Display(Name = nameof(GraphicsStrings.HslSecondary_HueCenter), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 360f)]
    public IProperty<float> HueCenter { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.HslSecondary_HueRange), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 180f)]
    public IProperty<float> HueRange { get; } = Property.CreateAnimatable(30f);

    [Display(Name = nameof(GraphicsStrings.HslSecondary_HueFeather), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 180f)]
    public IProperty<float> HueFeather { get; } = Property.CreateAnimatable(15f);

    [Display(Name = nameof(GraphicsStrings.HslSecondary_SaturationMin), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 100f)]
    public IProperty<float> SaturationMin { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.HslSecondary_SaturationMax), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 100f)]
    public IProperty<float> SaturationMax { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.HslSecondary_LuminanceMin), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 100f)]
    public IProperty<float> LuminanceMin { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.HslSecondary_LuminanceMax), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 100f)]
    public IProperty<float> LuminanceMax { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.HslSecondary_Feather), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 100f)]
    public IProperty<float> Feather { get; } = Property.CreateAnimatable(10f);

    [Display(Name = nameof(GraphicsStrings.HslSecondary_HueShift), ResourceType = typeof(GraphicsStrings))]
    [Range(-180f, 180f)]
    public IProperty<float> HueShift { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.HslSecondary_SaturationAdjust), ResourceType = typeof(GraphicsStrings))]
    [Range(-100f, 100f)]
    public IProperty<float> SaturationAdjust { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.HslSecondary_LuminanceAdjust), ResourceType = typeof(GraphicsStrings))]
    [Range(-100f, 100f)]
    public IProperty<float> LuminanceAdjust { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.HslSecondary_ShowMask), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> ShowMask { get; } = Property.CreateAnimatable(false);

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

            float satMin = Math.Clamp(data.SaturationMin, 0f, 100f) / 100f;
            float satMax = Math.Clamp(data.SaturationMax, 0f, 100f) / 100f;
            if (satMin > satMax) (satMin, satMax) = (satMax, satMin);

            float lumMin = Math.Clamp(data.LuminanceMin, 0f, 100f) / 100f;
            float lumMax = Math.Clamp(data.LuminanceMax, 0f, 100f) / 100f;
            if (lumMin > lumMax) (lumMin, lumMax) = (lumMax, lumMin);

            float feather = Math.Max(data.Feather, 0f) / 100f;

            float hueCenter = (((data.HueCenter % 360f) + 360f) % 360f) / 360f;
            float hueRange = Math.Clamp(data.HueRange, 0f, 180f) * 2f / 360f;
            float hueFeather = Math.Max(data.HueFeather, 0f) / 360f;

            builder.Children["src"] = baseShader;
            builder.Uniforms["hueCenter"] = hueCenter;
            builder.Uniforms["hueRange"] = hueRange;
            builder.Uniforms["hueFeather"] = hueFeather;
            builder.Uniforms["satMin"] = satMin;
            builder.Uniforms["satMax"] = satMax;
            builder.Uniforms["satFeather"] = feather;
            builder.Uniforms["lumMin"] = lumMin;
            builder.Uniforms["lumMax"] = lumMax;
            builder.Uniforms["lumFeather"] = feather;
            builder.Uniforms["hueShift"] = data.HueShift / 360f;
            builder.Uniforms["satAdjust"] = data.SaturationAdjust / 100f;
            builder.Uniforms["lumAdjust"] = data.LuminanceAdjust / 100f;
            builder.Uniforms["showMask"] = data.ShowMask ? 1f : 0f;

            context.Targets[i] = s_shader.ApplyToNewTarget(context, builder, target.Bounds);
        }
    }
}

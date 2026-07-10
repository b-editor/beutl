using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.ColorGrading), ResourceType = typeof(GraphicsStrings))]
public sealed partial class ColorGrading : FilterEffect
{
    // Fusable snippet; `c` is the premultiplied linear-light source pixel (contract A2).
    private static readonly string s_snippet =
        """
        uniform float exposure;
        uniform float contrast;
        uniform float contrastPivot;
        uniform float saturation;
        uniform float vibrance;
        uniform float hue;
        uniform float temperature;
        uniform float tint;
        uniform float3 shadows;
        uniform float3 midtones;
        uniform float3 highlights;
        uniform float3 lift;
        uniform float3 gamma;
        uniform float3 gain;
        uniform float3 offset;
        uniform float lowRange;
        uniform float highRange;

        const float3 LUMINANCE_COEFF = float3(0.2126, 0.7152, 0.0722);

        float get_luminance(float3 color) {
            return dot(color, LUMINANCE_COEFF);
        }

        float saturation_of(float3 color) {
            float maxc = max(max(color.r, color.g), color.b);
            float minc = min(min(color.r, color.g), color.b);
            float delta = maxc - minc;
            return maxc > 0.0 ? delta / maxc : 0.0;
        }

        float3 apply_lift_gamma_gain(float3 color, float3 l, float3 g, float3 gn) {
            color = color + l * (1.0 - color);

            float3 safe_gamma = max(g, float3(0.001));
            color = pow(max(color, float3(0.0)), 1.0 / safe_gamma);
            color *= gn;

            return color;
        }

        float3 apply_tonal_balance(float3 color, float3 shd, float3 mid, float3 hlt) {
            float luma = get_luminance(color);
            float shadow_w = 1.0 - smoothstep(0.0, lowRange, luma);
            float highlight_w = smoothstep(highRange, 1.0, luma);
            float midtone_w = 1.0 - shadow_w - highlight_w;

            midtone_w = max(midtone_w, 0.0);
            return color + shd * shadow_w + mid * midtone_w + hlt * highlight_w;
        }

        float3 apply_saturation(float3 color, float sat) {
            float luma = get_luminance(color);
            return mix(float3(luma), color, 1.0 + sat);
        }

        float3 apply_hue(float3 color, float hue) {
            float rad = radians(hue);
            float cos_a = cos(rad);
            float sin_a = sin(rad);

            const float3x3 rgb_to_yiq = float3x3(
                float3(0.299,  0.596,  0.212),
                float3(0.587, -0.275, -0.523),
                float3(0.114, -0.321,  0.311)
            );

            const float3x3 yiq_to_rgb = float3x3(
                float3(1.0,  1.0,  1.0),
                float3(0.956, -0.272, -1.105),
                float3(0.621, -0.647,  1.702)
            );

            float3x3 rotation = float3x3(
                float3(1.0, 0.0, 0.0),
                float3(0.0, cos_a, sin_a),
                float3(0.0, -sin_a, cos_a)
            );

            return yiq_to_rgb * (rotation * (rgb_to_yiq * color));
        }

        float3 apply_temperature_tint(float3 color, float temperature, float tint) {
            float3 temp_adjustment = float3(
                1.0 + temperature * 0.1,
                1.0,
                1.0 - temperature * 0.1
            );

            float3 tint_adjustment = float3(
                1.0 + tint * 0.05,
                1.0 - tint * 0.1,
                1.0 + tint * 0.05
            );

            return color * temp_adjustment * tint_adjustment;
        }

        half4 apply(half4 c) {
            float alpha = c.a;
            float3 color;
            if (alpha > 0.0001) {
                color = c.rgb / alpha;
            } else {
                return half4(0.0);
            }

            color *= exp2(exposure);
            color = apply_lift_gamma_gain(color, lift, gamma, gain);
            color = (color - contrastPivot) * (1.0 + contrast) + contrastPivot;
            color = apply_tonal_balance(color, shadows, midtones, highlights);
            color = apply_temperature_tint(color, temperature, tint);
            float satWeight = 1.0 - clamp(saturation_of(color), 0.0, 1.0);
            color = apply_saturation(color, saturation * (1.0 + vibrance * satWeight));
            color = apply_hue(color, hue);

            color += offset;

            return half4(color * alpha, alpha);
        }
        """;

    public ColorGrading()
    {
        ScanProperties<ColorGrading>();
    }

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Temperature), ResourceType = typeof(GraphicsStrings))]
    [Range(-100, 100)]
    public IProperty<float> Temperature { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Tint), ResourceType = typeof(GraphicsStrings))]
    [Range(-100, 100)]
    public IProperty<float> Tint { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Exposure), ResourceType = typeof(GraphicsStrings))]
    [Range(-5, 5), NumberStep(0.1, 0.01)]
    public IProperty<float> Exposure { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Contrast), ResourceType = typeof(GraphicsStrings))]
    [Range(-100, 100)]
    public IProperty<float> Contrast { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ColorGrading_ContrastPivot), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 1), NumberStep(0.1, 0.01)]
    public IProperty<float> ContrastPivot { get; } = Property.CreateAnimatable(0.5f);

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Saturation), ResourceType = typeof(GraphicsStrings))]
    [Range(-100, 100)]
    public IProperty<float> Saturation { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Vibrance), ResourceType = typeof(GraphicsStrings))]
    [Range(-100, 100)]
    public IProperty<float> Vibrance { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Hue), ResourceType = typeof(GraphicsStrings))]
    [Range(-180, 180)]
    public IProperty<float> Hue { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ColorGrading_LowRange), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IProperty<float> LowRange { get; } = Property.CreateAnimatable(40f);

    [Display(Name = nameof(GraphicsStrings.ColorGrading_HighRange), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IProperty<float> HighRange { get; } = Property.CreateAnimatable(60f);

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Shadows), ResourceType = typeof(GraphicsStrings))]
    public IProperty<GradingColor> Shadows { get; } = Property.CreateAnimatable(GradingColor.Zero);

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Midtones), ResourceType = typeof(GraphicsStrings))]
    public IProperty<GradingColor> Midtones { get; } = Property.CreateAnimatable(GradingColor.Zero);

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Highlights), ResourceType = typeof(GraphicsStrings))]
    public IProperty<GradingColor> Highlights { get; } = Property.CreateAnimatable(GradingColor.Zero);

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Lift), ResourceType = typeof(GraphicsStrings))]
    public IProperty<GradingColor> Lift { get; } = Property.CreateAnimatable(GradingColor.Zero);

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Gamma), ResourceType = typeof(GraphicsStrings))]
    public IProperty<GradingColor> Gamma { get; } = Property.CreateAnimatable(GradingColor.One);

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Gain), ResourceType = typeof(GraphicsStrings))]
    public IProperty<GradingColor> Gain { get; } = Property.CreateAnimatable(GradingColor.One);

    [Display(Name = nameof(GraphicsStrings.ColorGrading_Offset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<GradingColor> Offset { get; } = Property.CreateAnimatable(GradingColor.Zero);

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        float lowRange = Math.Clamp(r.LowRange, 0f, 100f);
        float highRange = Math.Clamp(r.HighRange, 0f, 100f);
        if (lowRange > highRange)
        {
            (lowRange, highRange) = (highRange, lowRange);
        }

        SKColorF shadows = ToColorVector(r.Shadows);
        SKColorF midtones = ToColorVector(r.Midtones);
        SKColorF highlights = ToColorVector(r.Highlights);
        SKColorF lift = ToColorVector(r.Lift);
        SKColorF gamma = ToColorVector(r.Gamma, 0.001f);
        SKColorF gain = ToColorVector(r.Gain, 0.0f);
        SKColorF offset = ToColorVector(r.Offset);

        builder.Shader(ShaderNodeDescriptor.Snippet(
            s_snippet,
            u => u.Float("exposure", r.Exposure)
                .Float("contrast", r.Contrast / 100f)
                .Float("contrastPivot", r.ContrastPivot)
                .Float("saturation", r.Saturation / 100f)
                .Float("vibrance", r.Vibrance / 100f)
                .Float("hue", r.Hue)
                .Float("temperature", r.Temperature / 100f)
                .Float("tint", r.Tint / 100f)
                .Float("lowRange", lowRange / 100f)
                .Float("highRange", highRange / 100f)
                .Float3("shadows", shadows.Red, shadows.Green, shadows.Blue)
                .Float3("midtones", midtones.Red, midtones.Green, midtones.Blue)
                .Float3("highlights", highlights.Red, highlights.Green, highlights.Blue)
                .Float3("lift", lift.Red, lift.Green, lift.Blue)
                .Float3("gamma", gamma.Red, gamma.Green, gamma.Blue)
                .Float3("gain", gain.Red, gain.Green, gain.Blue)
                .Float3("offset", offset.Red, offset.Green, offset.Blue)));
    }

    private static SKColorF ToColorVector(GradingColor value, float minValue = float.NegativeInfinity)
    {
        return new SKColorF(
            Math.Max(value.R, minValue),
            Math.Max(value.G, minValue),
            Math.Max(value.B, minValue),
            1f);
    }
}

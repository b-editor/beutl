using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.ColorGrading), ResourceType = typeof(GraphicsStrings))]
public sealed partial class ColorGrading : FilterEffect
{
    private const string ShaderSource =
        """
            uniform float exposure; // EV stops (-5 to +5)
            uniform float contrast; // -1 to +1
            uniform float contrastPivot; // typically 0.18 or 0.5
            uniform float saturation; // -1 to +1
            uniform float vibrance; // -1 to +1
            uniform float hue; // degrees (-180 to +180)
            uniform float temperature; // -1 to +1 (cool to warm)
            uniform float tint; // -1 to +1 (green to magenta)
            uniform float3 shadows; // RGB adjustment for shadows
            uniform float3 midtones; // RGB adjustment for midtones
            uniform float3 highlights; // RGB adjustment for highlights
            uniform float3 lift; // Shadow lift (typically -0.5 to +0.5)
            uniform float3 gamma; // Midtone gamma (typically 0.5 to 2.0, default 1.0)
            uniform float3 gain; // Highlight gain (typically 0.0 to 2.0, default 1.0)
            uniform float3 offset; // RGB offset (-1 to +1)
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

            float3 apply_hue(float3 color, float hueAmount) {
                float rad = radians(hueAmount);
                float cos_a = cos(rad);
                float sin_a = sin(rad);

                const float3x3 rgb_to_yiq = float3x3(
                    float3(0.299,  0.596,  0.212),   // column 0
                    float3(0.587, -0.275, -0.523),   // column 1
                    float3(0.114, -0.321,  0.311)    // column 2
                );

                const float3x3 yiq_to_rgb = float3x3(
                    float3(1.0,  1.0,  1.0),         // column 0
                    float3(0.956, -0.272, -1.105),   // column 1
                    float3(0.621, -0.647,  1.702)    // column 2
                );

                float3x3 rotation = float3x3(
                    float3(1.0, 0.0, 0.0),
                    float3(0.0, cos_a, sin_a),
                    float3(0.0, -sin_a, cos_a)
                );

                return yiq_to_rgb * (rotation * (rgb_to_yiq * color));
            }

            float3 apply_temperature_tint(float3 color, float temperatureAmount, float tintAmount) {
                float3 temp_adjustment = float3(
                    1.0 + temperatureAmount * 0.1,
                    1.0,
                    1.0 - temperatureAmount * 0.1
                );

                float3 tint_adjustment = float3(
                    1.0 + tintAmount * 0.05,
                    1.0 - tintAmount * 0.1,
                    1.0 + tintAmount * 0.05
                );

                return color * temp_adjustment * tint_adjustment;
            }

            half4 apply(half4 color) {
                float alpha = color.a;
                if (alpha <= 0.0001) return half4(0.0);
                float3 rgb = color.rgb / alpha;

                rgb *= exp2(exposure);
                rgb = apply_lift_gamma_gain(rgb, lift, gamma, gain);
                rgb = (rgb - contrastPivot) * (1.0 + contrast) + contrastPivot;
                rgb = apply_tonal_balance(rgb, shadows, midtones, highlights);
                rgb = apply_temperature_tint(rgb, temperature, tint);
                float satWeight = 1.0 - clamp(saturation_of(rgb), 0.0, 1.0);
                rgb = apply_saturation(rgb, saturation * (1.0 + vibrance * satWeight));
                rgb = apply_hue(rgb, hue);

                rgb += offset;

                return half4(rgb * alpha, alpha);
            }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.CurrentPixel);

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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        float lowRange = Math.Clamp(r.LowRange, 0f, 100f);
        float highRange = Math.Clamp(r.HighRange, 0f, 100f);
        if (lowRange > highRange)
        {
            (lowRange, highRange) = (highRange, lowRange);
        }

        context.Shader(ShaderDescription.CurrentPixel(
            s_shaderSource,
            bindings =>
            {
                bindings.Uniform("exposure", r.Exposure);
                bindings.Uniform("contrast", r.Contrast / 100f);
                bindings.Uniform("contrastPivot", r.ContrastPivot);
                bindings.Uniform("saturation", r.Saturation / 100f);
                bindings.Uniform("vibrance", r.Vibrance / 100f);
                bindings.Uniform("hue", r.Hue);
                bindings.Uniform("temperature", r.Temperature / 100f);
                bindings.Uniform("tint", r.Tint / 100f);
                bindings.Uniform("lowRange", lowRange / 100f);
                bindings.Uniform("highRange", highRange / 100f);
                bindings.Uniform("shadows", ToColorVector(r.Shadows));
                bindings.Uniform("midtones", ToColorVector(r.Midtones));
                bindings.Uniform("highlights", ToColorVector(r.Highlights));
                bindings.Uniform("lift", ToColorVector(r.Lift));
                bindings.Uniform("gamma", ToColorVector(r.Gamma, 0.001f));
                bindings.Uniform("gain", ToColorVector(r.Gain, 0.0f));
                bindings.Uniform("offset", ToColorVector(r.Offset));
            }));
    }

    private static Vector3 ToColorVector(GradingColor value, float minValue = float.NegativeInfinity)
        => new(
            Math.Max(value.R, minValue),
            Math.Max(value.G, minValue),
            Math.Max(value.B, minValue));
}

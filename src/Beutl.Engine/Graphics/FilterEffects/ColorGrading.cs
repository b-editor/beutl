using System.ComponentModel.DataAnnotations;
using System.Reactive;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed partial class ColorGrading : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<ColorGrading>();
    private static readonly SKRuntimeEffect? s_runtimeEffect;

    static ColorGrading()
    {
        const string sksl =
            """
            uniform shader src;
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
                float shadow_w = 1.0 - smoothstep(0.0, 0.4, luma);
                float highlight_w = smoothstep(0.6, 1.0, luma);
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

            half4 main(float2 coord) {
                half4 srcColor = src.eval(coord);
                float alpha = srcColor.a;
                float3 color;
                if (alpha > 0.0001) {
                    color = srcColor.rgb / alpha;
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

                color = clamp(color, 0.0, 1.0);
                return half4(color * alpha, alpha);
            }

            """;

        s_runtimeEffect = SKRuntimeEffect.CreateShader(sksl, out string? errorText);
        if (errorText is not null)
        {
            s_logger.LogError("Failed to compile color grading shader: {ErrorText}", errorText);
        }
    }

    public ColorGrading()
    {
        ScanProperties<ColorGrading>();
    }

    [Display(Name = nameof(Strings.Temperature), ResourceType = typeof(Strings))]
    [Range(-100, 100)]
    public IProperty<float> Temperature { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.Tint), ResourceType = typeof(Strings))]
    [Range(-100, 100)]
    public IProperty<float> Tint { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.Exposure), ResourceType = typeof(Strings))]
    [Range(-5, 5)]
    public IProperty<float> Exposure { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.Contrast), ResourceType = typeof(Strings))]
    [Range(-100, 100)]
    public IProperty<float> Contrast { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.ContrastPivot), ResourceType = typeof(Strings))]
    [Range(0, 1)]
    public IProperty<float> ContrastPivot { get; } = Property.CreateAnimatable(0.5f);

    [Display(Name = nameof(Strings.Saturation), ResourceType = typeof(Strings))]
    [Range(-100, 100)]
    public IProperty<float> Saturation { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.Vibrance), ResourceType = typeof(Strings))]
    [Range(-100, 100)]
    public IProperty<float> Vibrance { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.Hue), ResourceType = typeof(Strings))]
    [Range(-180, 180)]
    public IProperty<float> Hue { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.Shadows), ResourceType = typeof(Strings))]
    public IProperty<GradingColor> Shadows { get; } = Property.CreateAnimatable(GradingColor.Zero);

    [Display(Name = nameof(Strings.Midtones), ResourceType = typeof(Strings))]
    public IProperty<GradingColor> Midtones { get; } = Property.CreateAnimatable(GradingColor.Zero);

    [Display(Name = nameof(Strings.Highlights), ResourceType = typeof(Strings))]
    public IProperty<GradingColor> Highlights { get; } = Property.CreateAnimatable(GradingColor.Zero);

    [Display(Name = nameof(Strings.Lift), ResourceType = typeof(Strings))]
    public IProperty<GradingColor> Lift { get; } = Property.CreateAnimatable(GradingColor.Zero);

    [Display(Name = nameof(Strings.Gamma), ResourceType = typeof(Strings))]
    public IProperty<GradingColor> Gamma { get; } = Property.CreateAnimatable(GradingColor.One);

    [Display(Name = nameof(Strings.Gain), ResourceType = typeof(Strings))]
    public IProperty<GradingColor> Gain { get; } = Property.CreateAnimatable(GradingColor.One);

    [Display(Name = nameof(Strings.Offset), ResourceType = typeof(Strings))]
    public IProperty<GradingColor> Offset { get; } = Property.CreateAnimatable(GradingColor.Zero);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        if (s_runtimeEffect is null)
        {
            throw new InvalidOperationException("Failed to compile SKSL.");
        }

        var r = (Resource)resource;
        // TODO: 第二引数がIEquatableを要求しているので，タプルにしている
        context.CustomEffect(
            (r, Unit.Default),
            (t, c) => OnApply(t.r, c),
            static (_, rect) => rect);
    }

    private static void OnApply(Resource data, CustomFilterEffectContext context)
    {
        if (s_runtimeEffect is null)
        {
            return;
        }

        for (int i = 0; i < context.Targets.Count; i++)
        {
            var target = context.Targets[i];
            if (target.RenderTarget is not { } renderTarget)
                continue;

            using SKImage image = renderTarget.Value.Snapshot();
            using SKShader baseShader = SKShader.CreateImage(image, SKShaderTileMode.Decal, SKShaderTileMode.Decal);
            var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);

            builder.Children["src"] = baseShader;
            builder.Uniforms["exposure"] = data.Exposure;
            builder.Uniforms["contrast"] = data.Contrast / 100f;
            builder.Uniforms["contrastPivot"] = data.ContrastPivot;
            builder.Uniforms["saturation"] = data.Saturation / 100f;
            builder.Uniforms["vibrance"] = data.Vibrance / 100f;
            builder.Uniforms["hue"] = data.Hue;
            builder.Uniforms["temperature"] = data.Temperature / 100f;
            builder.Uniforms["tint"] = data.Tint / 100f;
            builder.Uniforms["shadows"] = ToColorVector(data.Shadows);
            builder.Uniforms["midtones"] = ToColorVector(data.Midtones);
            builder.Uniforms["highlights"] = ToColorVector(data.Highlights);
            builder.Uniforms["lift"] = ToColorVector(data.Lift);
            builder.Uniforms["gamma"] = ToColorVector(data.Gamma, 0.001f);
            builder.Uniforms["gain"] = ToColorVector(data.Gain, 0.0f);
            builder.Uniforms["offset"] = ToColorVector(data.Offset);

            using SKShader finalShader = builder.Build();
            using var paint = new SKPaint { Shader = finalShader };

            EffectTarget newTarget = context.CreateTarget(target.Bounds);
            var canvas = newTarget.RenderTarget!.Value.Canvas;
            canvas.DrawRect(new SKRect(0, 0, newTarget.Bounds.Width, newTarget.Bounds.Height), paint);

            context.Targets[i] = newTarget;
            target.Dispose();
        }
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

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
            uniform float exposure;
            uniform float contrast;
            uniform float contrastPivot;
            uniform float saturation;
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

            float3 apply_tonal_balance(float3 color, float3 shadows, float3 midtones, float3 highlights) {
                const float3 luminance = float3(0.2126, 0.7152, 0.0722);
                float luma = dot(color, luminance);

                float shadow_w = 1.0 - smoothstep(0.0, 0.5, luma);
                float highlight_w = smoothstep(0.5, 1.0, luma);
                float midtone_w = 1.0 - shadow_w - highlight_w;

                return color + shadows * shadow_w + midtones * midtone_w + highlights * highlight_w;
            }

            float3 apply_saturation(float3 color, float sat) {
                const float3 luminance = float3(0.2126, 0.7152, 0.0722);
                float luma = dot(color, luminance);
                return mix(float3(luma, luma, luma), color, 1.0 + sat);
            }

            float3 apply_hue(float3 color, float hue) {
                float rad = radians(hue);
                float cos_a = cos(rad);
                float sin_a = sin(rad);

                const float3x3 rgb_to_yiq = float3x3(
                    0.299, 0.587, 0.114,
                    0.596, -0.275, -0.321,
                    0.212, -0.523, 0.311);

                const float3x3 yiq_to_rgb = float3x3(
                    1.0, 0.956, 0.621,
                    1.0, -0.272, -0.647,
                    1.0, -1.105, 1.702);

                float3x3 rotation = float3x3(
                    1.0, 0.0, 0.0,
                    0.0, cos_a, -sin_a,
                    0.0, sin_a, cos_a);

                return yiq_to_rgb * (rotation * (rgb_to_yiq * color));
            }

            float3 apply_temperature_tint(float3 color, float temperature, float tint) {
                float warm = temperature * 0.1;
                float green = tint * 0.1;
                float magenta = tint * 0.05;

                color.r += warm - magenta;
                color.g += green;
                color.b -= warm + magenta;

                return color;
            }

            float3 apply_lift_gamma_gain(float3 color, float3 lift, float3 gamma, float3 gain) {
                color = max(color + lift, 0.0);
                color = pow(color, 1.0 / max(gamma, float3(0.001)));
                color *= gain;
                return color;
            }

            half4 main(float2 coord) {
                half4 srcColor = src.eval(coord);
                float3 color = srcColor.rgb;

                color *= exp2(exposure);
                color = (color - contrastPivot) * (1.0 + contrast) + contrastPivot;
                color = apply_tonal_balance(color, shadows, midtones, highlights);
                color = apply_saturation(color, saturation);
                color = apply_hue(color, hue);
                color = apply_temperature_tint(color, temperature, tint);
                color = apply_lift_gamma_gain(color, lift, gamma, gain);
                color += offset;

                color = clamp(color, 0.0, 1.0);
                return half4(color, srcColor.a);
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

    [Display(Name = nameof(Strings.Hue), ResourceType = typeof(Strings))]
    [Range(-180, 180)]
    public IProperty<float> Hue { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.Shadows), ResourceType = typeof(Strings))]
    public IProperty<Color> Shadows { get; } = Property.CreateAnimatable(Colors.Transparent);

    [Display(Name = nameof(Strings.Midtones), ResourceType = typeof(Strings))]
    public IProperty<Color> Midtones { get; } = Property.CreateAnimatable(Colors.Transparent);

    [Display(Name = nameof(Strings.Highlights), ResourceType = typeof(Strings))]
    public IProperty<Color> Highlights { get; } = Property.CreateAnimatable(Colors.Transparent);

    [Display(Name = nameof(Strings.Lift), ResourceType = typeof(Strings))]
    public IProperty<Color> Lift { get; } = Property.CreateAnimatable(Colors.Transparent);

    [Display(Name = nameof(Strings.Gamma), ResourceType = typeof(Strings))]
    public IProperty<Color> Gamma { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(Strings.Gain), ResourceType = typeof(Strings))]
    public IProperty<Color> Gain { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(Strings.Offset), ResourceType = typeof(Strings))]
    public IProperty<Color> Offset { get; } = Property.CreateAnimatable(Colors.Transparent);

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
            using var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);

            builder.Children["src"] = baseShader;
            builder.Uniforms["exposure"] = data.Exposure;
            builder.Uniforms["contrast"] = data.Contrast / 100f;
            builder.Uniforms["contrastPivot"] = data.ContrastPivot;
            builder.Uniforms["saturation"] = data.Saturation / 100f;
            builder.Uniforms["hue"] = data.Hue;
            builder.Uniforms["temperature"] = data.Temperature / 100f;
            builder.Uniforms["tint"] = data.Tint / 100f;
            builder.Uniforms["shadows"] = ToColorVector(data.Shadows);
            builder.Uniforms["midtones"] = ToColorVector(data.Midtones);
            builder.Uniforms["highlights"] = ToColorVector(data.Highlights);
            builder.Uniforms["lift"] = ToColorVector(data.Lift);
            builder.Uniforms["gamma"] = ToColorVector(data.Gamma, 1f / 255f, 0.001f);
            builder.Uniforms["gain"] = ToColorVector(data.Gain, 1f / 255f, 0.0f);
            builder.Uniforms["offset"] = ToColorVector(data.Offset);

            using SKShader finalShader = builder.Build();
            using var paint = new SKPaint { Shader = finalShader };

            EffectTarget newTarget = context.CreateTarget(target.Bounds);
            using ImmediateCanvas canvas = context.Open(newTarget);
            canvas.Canvas.DrawRect(new SKRect(0, 0, newTarget.Bounds.Width, newTarget.Bounds.Height), paint);

            context.Targets[i] = newTarget;
            target.Dispose();
        }
    }

    private static SKColorF ToColorVector(Color value, float scale = 1f / 255f, float minValue = 0f)
    {
        return new SKColorF(
            Math.Max(value.R * scale, minValue),
            Math.Max(value.G * scale, minValue),
            Math.Max(value.B * scale, minValue),
            value.A * scale);
    }
}

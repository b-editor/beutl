using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.Curves), ResourceType = typeof(Strings))]
public sealed partial class Curves : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<Curves>();
    private static readonly SKRuntimeEffect? s_runtimeEffect;

    static Curves()
    {
        const string sksl =
            """
            uniform shader src;
            uniform shader masterCurve;
            uniform shader redCurve;
            uniform shader greenCurve;
            uniform shader blueCurve;
            uniform shader hueVsHue;
            uniform shader hueVsSat;
            uniform shader hueVsLuma;
            uniform shader lumaVsSat;
            uniform shader satVsSat;

            const float3 LUMA = float3(0.2126, 0.7152, 0.0722);

            float3 rgb_to_hsv(float3 c)
            {
                float4 K = float4(0., -1./3., 2./3., -1.);
                float4 p = mix(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = mix(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

                float d = q.x - min(q.w, q.y);
                float e = 1e-10;
                return float3(abs(q.z + (q.w - q.y) / (6. * d + e)), d / (q.x + e), q.x);
            }

            float3 hsv_to_rgb(float3 c)
            {
                float4 K = float4(1., 2./3., 1./3., 3.);
                float3 p = abs(fract(c.xxx + K.xyz) * 6. - K.www);
                return c.z * mix(K.xxx, clamp(p - K.xxx, 0., 1.), c.y);
            }

            half4 main(float2 coord) {
                half4 baseColor = src.eval(coord);
                if (baseColor.a <= 0.0001) return baseColor;

                float3 rgb = baseColor.rgb / baseColor.a;
                float luma = dot(rgb, LUMA);
                float3 hsv = rgb_to_hsv(rgb);

                float hueShift = hueVsHue.eval(float2(hsv.x, 0.5)).a - 0.5;
                hsv.x = fract(hsv.x + hueShift + 1.0);

                // Curve value 0.5 = no change, 0.0 = 0x, 1.0 = 2x
                hsv.y *= hueVsSat.eval(float2(hsv.x, 0.5)).a * 2.0;
                hsv.z *= hueVsLuma.eval(float2(hsv.x, 0.5)).a * 2.0;

                hsv.y *= lumaVsSat.eval(float2(luma, 0.5)).a * 2.0;
                hsv.y = clamp(hsv.y, 0.0, 1.0);

                hsv.y *= satVsSat.eval(float2(hsv.y, 0.5)).a * 2.0;
                hsv.y = clamp(hsv.y, 0.0, 1.0);

                rgb = hsv_to_rgb(hsv);

                rgb.r = redCurve.eval(float2(rgb.r, 0.5)).a;
                rgb.g = greenCurve.eval(float2(rgb.g, 0.5)).a;
                rgb.b = blueCurve.eval(float2(rgb.b, 0.5)).a;

                rgb.r = masterCurve.eval(float2(rgb.r, 0.5)).a;
                rgb.g = masterCurve.eval(float2(rgb.g, 0.5)).a;
                rgb.b = masterCurve.eval(float2(rgb.b, 0.5)).a;

                rgb = clamp(rgb, 0.0, 1.0);
                return half4(rgb * baseColor.a, baseColor.a);
            }
            """;

        s_runtimeEffect = SKRuntimeEffect.CreateShader(sksl, out string? errorText);
        if (errorText is not null)
        {
            s_logger.LogError("Failed to compile curves shader: {ErrorText}", errorText);
        }
    }

    public Curves()
    {
        ScanProperties<Curves>();
    }

    [Display(Name = nameof(Strings.CustomCurve), ResourceType = typeof(Strings))]
    public IProperty<CurveMap> MasterCurve { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0), new CurveControlPoint(1, 1)]));

    [Display(Name = nameof(Strings.RedCurve), ResourceType = typeof(Strings))]
    public IProperty<CurveMap> RedCurve { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0), new CurveControlPoint(1, 1)]));

    [Display(Name = nameof(Strings.GreenCurve), ResourceType = typeof(Strings))]
    public IProperty<CurveMap> GreenCurve { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0), new CurveControlPoint(1, 1)]));

    [Display(Name = nameof(Strings.BlueCurve), ResourceType = typeof(Strings))]
    public IProperty<CurveMap> BlueCurve { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0), new CurveControlPoint(1, 1)]));

    [Display(Name = nameof(Strings.HueVsHue), ResourceType = typeof(Strings))]
    public IProperty<CurveMap> HueVsHue { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0.5f), new CurveControlPoint(1, 0.5f)]));

    [Display(Name = nameof(Strings.HueVsSaturation), ResourceType = typeof(Strings))]
    public IProperty<CurveMap> HueVsSaturation { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0.5f), new CurveControlPoint(1, 0.5f)]));

    [Display(Name = nameof(Strings.HueVsLuminance), ResourceType = typeof(Strings))]
    public IProperty<CurveMap> HueVsLuminance { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0.5f), new CurveControlPoint(1, 0.5f)]));

    [Display(Name = nameof(Strings.LuminanceVsSaturation), ResourceType = typeof(Strings))]
    public IProperty<CurveMap> LuminanceVsSaturation { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0.5f), new CurveControlPoint(1, 0.5f)]));

    [Display(Name = nameof(Strings.SaturationVsSaturation), ResourceType = typeof(Strings))]
    public IProperty<CurveMap> SaturationVsSaturation { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0.5f), new CurveControlPoint(1, 0.5f)]));

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        if (s_runtimeEffect is null)
        {
            throw new InvalidOperationException("Failed to compile SKSL.");
        }

        var r = (Resource)resource;

        context.CustomEffect(
            (Resource: r, Dummy: 0),
            static (data, ctx) => OnApply(data.Resource, ctx),
            static (_, rect) => rect);
    }

    private static void OnApply(Resource data, CustomFilterEffectContext context)
    {
        if (s_runtimeEffect is null)
            return;

        using SKShader master = data.MasterCurve.ToShader();
        using SKShader red = data.RedCurve.ToShader();
        using SKShader green = data.GreenCurve.ToShader();
        using SKShader blue = data.BlueCurve.ToShader();
        using SKShader hueHue = data.HueVsHue.ToShader();
        using SKShader hueSat = data.HueVsSaturation.ToShader();
        using SKShader hueLum = data.HueVsLuminance.ToShader();
        using SKShader lumSat = data.LuminanceVsSaturation.ToShader();
        using SKShader satSat = data.SaturationVsSaturation.ToShader();

        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget target = context.Targets[i];
            if (target.RenderTarget is not { } renderTarget)
                continue;

            using SKImage image = renderTarget.Value.Snapshot();
            using SKShader baseShader = SKShader.CreateImage(image, SKShaderTileMode.Decal, SKShaderTileMode.Decal);

            var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);

            builder.Children["src"] = baseShader;
            builder.Children["masterCurve"] = master;
            builder.Children["redCurve"] = red;
            builder.Children["greenCurve"] = green;
            builder.Children["blueCurve"] = blue;
            builder.Children["hueVsHue"] = hueHue;
            builder.Children["hueVsSat"] = hueSat;
            builder.Children["hueVsLuma"] = hueLum;
            builder.Children["lumaVsSat"] = lumSat;
            builder.Children["satVsSat"] = satSat;

            EffectTarget newTarget = context.CreateTarget(target.Bounds);
            using (SKShader finalShader = builder.Build())
            using (var paint = new SKPaint { Shader = finalShader })
            using (var canvas = context.Open(newTarget))
            {
                canvas.Clear();
                canvas.Canvas.DrawRect(new SKRect(0, 0, newTarget.Bounds.Width, newTarget.Bounds.Height), paint);
                context.Targets[i] = newTarget;
            }

            target.Dispose();
        }
    }
}

using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Curves), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Curves : FilterEffect
{
    // Fusable snippet; `c` is the premultiplied linear-light source pixel (contract A2).
    // The nine curve samplers are extra textures (their contents are parameters, A4).
    private static readonly string s_snippet =
        """
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

        float3 rgb_to_hsv(float3 c) {
            float4 K = float4(0., -1./3., 2./3., -1.);
            float4 p = mix(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
            float4 q = mix(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

            float d = q.x - min(q.w, q.y);
            float e = 1e-10;
            return float3(abs(q.z + (q.w - q.y) / (6. * d + e)), d / (q.x + e), q.x);
        }

        float3 hsv_to_rgb(float3 c) {
            float4 K = float4(1., 2./3., 1./3., 3.);
            float3 p = abs(fract(c.xxx + K.xyz) * 6. - K.www);
            return c.z * mix(K.xxx, clamp(p - K.xxx, 0., 1.), c.y);
        }

        float3 linearToSrgb(float3 c) {
            float3 lo = c * 12.92;
            float3 hi = 1.055 * pow(c, float3(1.0/2.4)) - 0.055;
            return mix(lo, hi, step(float3(0.0031308), c));
        }

        float3 srgbToLinear(float3 c) {
            float3 lo = c / 12.92;
            float3 hi = pow((c + 0.055) / 1.055, float3(2.4));
            return mix(lo, hi, step(float3(0.04045), c));
        }

        half4 apply(half4 c) {
            if (c.a <= 0.0001) return c;

            float3 rgb = linearToSrgb(c.rgb / c.a);
            float luma = dot(rgb, LUMA);
            float3 hsv = rgb_to_hsv(rgb);

            float hueShift = hueVsHue.eval(float2(hsv.x, 0.5)).a - 0.5;
            hsv.x = fract(hsv.x + hueShift + 1.0);

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

            float3 result = srgbToLinear(rgb);
            return half4(half3(result * c.a), c.a);
        }
        """;

    public Curves()
    {
        ScanProperties<Curves>();
    }

    [Display(Name = nameof(GraphicsStrings.Curves_MasterCurve), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CurveMap> MasterCurve { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0), new CurveControlPoint(1, 1)]));

    [Display(Name = nameof(GraphicsStrings.Curves_RedCurve), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CurveMap> RedCurve { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0), new CurveControlPoint(1, 1)]));

    [Display(Name = nameof(GraphicsStrings.Curves_GreenCurve), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CurveMap> GreenCurve { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0), new CurveControlPoint(1, 1)]));

    [Display(Name = nameof(GraphicsStrings.Curves_BlueCurve), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CurveMap> BlueCurve { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0), new CurveControlPoint(1, 1)]));

    [Display(Name = nameof(GraphicsStrings.Curves_HueVsHue), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CurveMap> HueVsHue { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0.5f), new CurveControlPoint(1, 0.5f)]));

    [Display(Name = nameof(GraphicsStrings.Curves_HueVsSaturation), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CurveMap> HueVsSaturation { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0.5f), new CurveControlPoint(1, 0.5f)]));

    [Display(Name = nameof(GraphicsStrings.Curves_HueVsLuminance), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CurveMap> HueVsLuminance { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0.5f), new CurveControlPoint(1, 0.5f)]));

    [Display(Name = nameof(GraphicsStrings.Curves_LuminanceVsSaturation), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CurveMap> LuminanceVsSaturation { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0.5f), new CurveControlPoint(1, 0.5f)]));

    [Display(Name = nameof(GraphicsStrings.Curves_SaturationVsSaturation), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CurveMap> SaturationVsSaturation { get; } = Property.Create(new CurveMap([new CurveControlPoint(0, 0.5f), new CurveControlPoint(1, 0.5f)]));

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        builder.Shader(ShaderNodeDescriptor.Snippet(
            s_snippet,
            samplers:
            [
                new ChildBinding("masterCurve", r.GetOrBuildCurveShader(0, r.MasterCurve)),
                new ChildBinding("redCurve", r.GetOrBuildCurveShader(1, r.RedCurve)),
                new ChildBinding("greenCurve", r.GetOrBuildCurveShader(2, r.GreenCurve)),
                new ChildBinding("blueCurve", r.GetOrBuildCurveShader(3, r.BlueCurve)),
                new ChildBinding("hueVsHue", r.GetOrBuildCurveShader(4, r.HueVsHue)),
                new ChildBinding("hueVsSat", r.GetOrBuildCurveShader(5, r.HueVsSaturation)),
                new ChildBinding("hueVsLuma", r.GetOrBuildCurveShader(6, r.HueVsLuminance)),
                new ChildBinding("lumaVsSat", r.GetOrBuildCurveShader(7, r.LuminanceVsSaturation)),
                new ChildBinding("satVsSat", r.GetOrBuildCurveShader(8, r.SaturationVsSaturation)),
            ]));
    }

    public new partial class Resource
    {
        private CurveMap?[] _cachedCurves = new CurveMap?[9];
        private SKShader?[] _cachedCurveShaders = new SKShader?[9];

        internal int CurveShaderBuildCountForTest { get; private set; }

        internal SKShader GetOrBuildCurveShader(int slot, CurveMap curve)
            => GetOrBuildCurveShaderCore(
                slot,
                curve,
                static value => value.ToShader(),
                static shader => shader.Dispose());

        internal SKShader GetOrBuildCurveShaderForTest(
            int slot,
            CurveMap curve,
            Func<CurveMap, SKShader> shaderFactory,
            Action<SKShader> shaderDisposer)
        {
            ArgumentNullException.ThrowIfNull(shaderFactory);
            ArgumentNullException.ThrowIfNull(shaderDisposer);
            return GetOrBuildCurveShaderCore(slot, curve, shaderFactory, shaderDisposer);
        }

        private SKShader GetOrBuildCurveShaderCore(
            int slot,
            CurveMap curve,
            Func<CurveMap, SKShader> shaderFactory,
            Action<SKShader> shaderDisposer)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(slot);
            if (slot >= _cachedCurves.Length)
                throw new ArgumentOutOfRangeException(nameof(slot));

            SKShader? shader = _cachedCurveShaders[slot];
            if (shader is null || !ReferenceEquals(_cachedCurves[slot], curve))
            {
                // Build and publish the replacement before retiring the previous shader. A construction failure
                // leaves the old key/shader pair untouched; a disposal failure leaves the new pair coherent and is
                // still propagated to the caller by identity.
                SKShader replacement = shaderFactory(curve);
                _cachedCurves[slot] = curve;
                _cachedCurveShaders[slot] = replacement;
                CurveShaderBuildCountForTest++;
                if (shader != null && !ReferenceEquals(shader, replacement))
                    shaderDisposer(shader);
                shader = replacement;
            }

            return shader;
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            SKShader?[] cachedCurveShaders = _cachedCurveShaders;
            _cachedCurveShaders = new SKShader?[9];
            CurveMap?[] cachedCurves = _cachedCurves;
            _cachedCurves = new CurveMap?[9];
            CurveShaderBuildCountForTest = 0;

            Exception? failure = null;
            DisposeOwnedResources(ref failure, cachedCurveShaders);
            Array.Clear(cachedCurveShaders);
            Array.Clear(cachedCurves);
            ThrowIfCleanupFailed(failure);
        }
    }
}

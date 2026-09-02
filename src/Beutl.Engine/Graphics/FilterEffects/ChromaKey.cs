using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.ChromaKey), ResourceType = typeof(GraphicsStrings))]
public partial class ChromaKey : FilterEffect
{
    private const string ShaderSource =
        """
        uniform float3 keyColor;
        uniform float3 keyColorLinear;
        uniform float hueRange;
        uniform float saturationRange;
        uniform float boundary;

        // Match in linear light, where the 8-bit quantization error is uniform. Include half-storage error;
        // premultiplication makes the bound alpha-independent but intentionally dominates at low alpha.
        const float kLinearQuantum = 0.5 / 255.0;
        const float kHalfStorageUlp = 1.0 / 2048.0;

        // Hue divides by the chroma, and that same quantization can manufacture one linear code of chroma, so
        // a key colour below one code has no dependable hue and full confidence only above two. Only the key
        // is measured: withholding the hue term is a vote to remove, because the shader removes what no term
        // claims, and a pixel whose own chroma is low is thereby known not to be a chromatic key.
        const half kHueChromaFloor = 1.0 / 255.0;

        // Slack for content that arrives near, but not on, the key colour, and the narrowest smoothstep
        // this shader will run: equal edges are undefined in the shading languages, and Boundary 0 is the
        // natural authoring choice for a hard key.
        const half kEdgeTolerance = 1.0 / 255.0;

        half3 rgb2hsv(half3 value) {
            half r = value.r;
            half g = value.g;
            half b = value.b;
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
                h = h / 6.0;
            }

            half s = (maxc <= 0.0) ? 0.0 : (delta / maxc);
            half v = maxc;
            return half3(h, s, v);
        }

        half3 linearToSrgb(half3 value) {
            half3 lo = value * 12.92;
            half3 hi = 1.055 * pow(value, half3(1.0 / 2.4)) - 0.055;
            return mix(lo, hi, step(half3(0.0031308), value));
        }

        half chroma(half3 value) {
            return max(value.r, max(value.g, value.b)) - min(value.r, min(value.g, value.b));
        }

        half4 apply(half4 color) {
            half alpha = color.a;
            if (alpha <= 0.0001) return half4(0.0);
            half3 rgb = color.rgb / alpha;

            float3 keyPremul = keyColorLinear * float(alpha);
            float3 excess = abs(float3(color.rgb) - keyPremul)
                - (kLinearQuantum + (kHalfStorageUlp * keyPremul));
            half onKeyColor = max(excess.r, max(excess.g, excess.b)) <= 0.0 ? 1.0 : 0.0;

            half3 hsv = rgb2hsv(linearToSrgb(rgb));
            half3 keyHSV = rgb2hsv(keyColor);

            half hueDiff = abs(hsv.x - keyHSV.x);
            hueDiff = min(hueDiff, 1.0 - hueDiff);

            half satDiff = abs(hsv.y - keyHSV.y);

            half width = max(boundary, kEdgeTolerance);
            half hueEdge0 = hueRange + kEdgeTolerance;
            half satEdge0 = saturationRange + kEdgeTolerance;
            half hueSignal = smoothstep(
                kHueChromaFloor,
                2.0 * kHueChromaFloor,
                chroma(half3(keyColorLinear)));
            half maskHue = smoothstep(hueEdge0, hueEdge0 + width, hueDiff) * hueSignal;
            half maskSat = smoothstep(satEdge0, satEdge0 + width, satDiff);
            half mask = max(maskHue, maskSat) * (1.0 - onKeyColor);

            return color * mask;
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.CurrentPixel);

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
        Vector4 linear = r.Color.ToLinear();
        context.Shader(ShaderDescription.CurrentPixel(
            s_shaderSource,
            bindings =>
            {
                bindings.Uniform(
                    "keyColor",
                    new Vector3(
                        r.Color.R / 255f,
                        r.Color.G / 255f,
                        r.Color.B / 255f));
                bindings.Uniform("keyColorLinear", new Vector3(linear.X, linear.Y, linear.Z));
                bindings.Uniform("hueRange", r.HueRange / 360f);
                bindings.Uniform("saturationRange", r.SaturationRange / 100f);
                bindings.Uniform("boundary", r.Boundary / 100f);
            }));
    }
}

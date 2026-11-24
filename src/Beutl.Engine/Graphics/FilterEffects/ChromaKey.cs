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
        uniform float spillSuppression;
        uniform float3 spillAxis;
        uniform float secondaryWeight;
        uniform float3 weakAxis;

        // Match in linear light, where 8-bit quantization error is uniform, plus the half-storage error.
        const float kLinearQuantum = 0.5 / 255.0;
        const float kHalfStorageUlp = 1.0 / 2048.0;

        // Quantization can manufacture one code of chroma, so a key below one code has no dependable hue;
        // only the key is measured, and withholding the hue term votes to remove.
        const half kHueChromaFloor = 1.0 / 255.0;

        // Slack around the key colour, and the narrowest smoothstep: equal edges are undefined.
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
            half3 hi = 1.055 * pow(max(value, half3(0.0)), half3(1.0 / 2.4)) - 0.055;
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

            // Preserve the original premultiplied channels when there is no suppression.
            float amount = spillSuppression * float(hueSignal);
            if (amount <= 0.0 || mask == 0.0) return color * mask;

            // The limit is a min of averages, which scales with alpha, so despill the premultiplied
            // channels directly and leave the other two untouched by an unpremultiply round trip.
            float3 premulRgb = float3(color.rgb);
            float channel = dot(premulRgb, spillAxis);
            float limit = dot(premulRgb, float3(1.0) - spillAxis) * 0.5;
            float3 despilled = mix(premulRgb, float3(min(channel, limit)), spillAxis * amount);
            if (secondaryWeight > 0.0) {
                // A secondary key spills into its two strong channels, so limit both to its weak one.
                float3 limited = min(premulRgb, float3(dot(premulRgb, weakAxis)));
                despilled = mix(despilled, mix(premulRgb, limited, amount), secondaryWeight);
            }
            return half4(half3(despilled), alpha) * mask;
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.CurrentPixel);

    public ChromaKey()
    {
        ScanProperties<ChromaKey>();
    }

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.Black);

    [Display(Name = nameof(GraphicsStrings.ChromaKey_HueRange), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> HueRange { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ChromaKey_SaturationRange), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> SaturationRange { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.ChromaKey_Boundary), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Boundary { get; } = Property.CreateAnimatable(2f);

    [Display(Name = nameof(GraphicsStrings.ChromaKey_SpillSuppression), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> SpillSuppression { get; } = Property.CreateAnimatable<float>();

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.Shader(ShaderDescription.CurrentPixel(
                s_shaderSource,
                bindings =>
                {
                    Vector3 keyColorLinear = Color.ToLinear().AsVector3();
                    // Prefer green, then blue when the dominant channels tie.
                    Vector3 spillAxis = keyColorLinear.Y >= MathF.Max(keyColorLinear.X, keyColorLinear.Z)
                        ? Vector3.UnitY
                        : keyColorLinear.Z >= keyColorLinear.X ? Vector3.UnitZ : Vector3.UnitX;
                    float spillSuppression = float.IsNaN(SpillSuppression) ? 0f : Math.Clamp(SpillSuppression / 100f, 0f, 1f);
                    (float secondaryWeight, Vector3 weakAxis) = GetSecondarySpill(keyColorLinear);

                    bindings.Uniform(
                        "keyColor",
                        new Vector3(
                            Color.R / 255f,
                            Color.G / 255f,
                            Color.B / 255f));
                    bindings.Uniform("keyColorLinear", keyColorLinear);
                    bindings.Uniform("hueRange", HueRange / 360f);
                    bindings.Uniform("saturationRange", SaturationRange / 100f);
                    bindings.Uniform("boundary", Boundary / 100f);
                    bindings.Uniform("spillSuppression", spillSuppression);
                    bindings.Uniform("spillAxis", spillAxis);
                    bindings.Uniform("secondaryWeight", secondaryWeight);
                    bindings.Uniform("weakAxis", weakAxis);
                }));
        }

        // How far the key is from a primary toward a secondary color, by the middle channel's share of its chroma,
        // and the key's weakest channel. The weight stays exactly 0 for green and blue screens, which sit near a primary.
        private static (float Weight, Vector3 WeakAxis) GetSecondarySpill(Vector3 key)
        {
            float max = MathF.Max(key.X, MathF.Max(key.Y, key.Z));
            float min = MathF.Min(key.X, MathF.Min(key.Y, key.Z));
            if (!(max > min)) return (0f, Vector3.Zero);

            float middle = key.X + key.Y + key.Z - max - min;
            float t = Math.Clamp(((middle - min) / (max - min) - 0.25f) / 0.5f, 0f, 1f);
            Vector3 weakAxis = key.X == min ? Vector3.UnitX : key.Y == min ? Vector3.UnitY : Vector3.UnitZ;
            return (t * t * (3f - 2f * t), weakAxis);
        }
    }
}

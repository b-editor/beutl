using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[TestFixture]
[NonParallelizable]
public sealed class ChromaKeySpillSuppressionTests
{
    private const float Tolerance = 0.001f;
    private static readonly PixelSize s_frame = new(16, 16);
    private static readonly Color s_greenishFill = Color.FromArgb(160, 120, 170, 110);

    // ChromaKey's shader as it shipped before spill suppression, frozen here so that 0% is held to the
    // pixels existing projects already render rather than to the current shader itself.
    private const string PreSpillShaderSource =
        """
        uniform float3 keyColor;
        uniform float3 keyColorLinear;
        uniform float hueRange;
        uniform float saturationRange;
        uniform float boundary;

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

            return color * mask;
        }
        """;

    [TestCaseSource(nameof(ZeroSuppressionCases))]
    public void ZeroSuppression_RendersLikeThePreSpillShader(Color fill, Color key, float hueRange, float saturationRange, float boundary)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new ChromaKey
            {
                Color = { Value = key },
                HueRange = { Value = hueRange },
                SaturationRange = { Value = saturationRange },
                Boundary = { Value = boundary },
                SpillSuppression = { Value = 0f },
            };
            var reference = new EffectItemSuffixCallbackFilterEffect((context, _) =>
                context.Shader(ShaderDescription.CurrentPixel(
                    PreSpillShaderSource,
                    bindings =>
                    {
                        bindings.Uniform("keyColor", new Vector3(key.R / 255f, key.G / 255f, key.B / 255f));
                        bindings.Uniform("keyColorLinear", key.ToLinear().AsVector3());
                        bindings.Uniform("hueRange", hueRange / 360f);
                        bindings.Uniform("saturationRange", saturationRange / 100f);
                        bindings.Uniform("boundary", boundary / 100f);
                    })));

            using Bitmap expected = RenderEffect(fill, reference);
            using Bitmap actual = RenderEffect(fill, effect);
            GoldenImageHarness.AssertByteIdentical(
                expected,
                actual,
                $"rgba({fill.R},{fill.G},{fill.B},{fill.A}) keyed by rgb({key.R},{key.G},{key.B})");
        });
    }

    private static IEnumerable<object[]> ZeroSuppressionCases()
    {
        yield return [s_greenishFill, Colors.Lime, 0f, 0f, 2f];
        yield return [s_greenishFill, Colors.Lime, 20f, 30f, 10f];
        yield return [Color.FromArgb(160, 80, 160, 170), Colors.Blue, 0f, 0f, 2f];
        yield return [Color.FromArgb(200, 60, 200, 70), Colors.Lime, 30f, 40f, 20f];
        yield return [Colors.Lime, Colors.Lime, 0f, 0f, 2f];
        yield return [s_greenishFill, Colors.Black, 0f, 0f, 2f];
    }

    [Test]
    public void LimeKey_BlendsGreenTowardTheOtherChannelsInLinearLight()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap baseline = Render(s_greenishFill, Colors.Lime);
            using Bitmap zero = Render(s_greenishFill, Colors.Lime, 0f);
            using Bitmap half = Render(s_greenishFill, Colors.Lime, 50f);
            using Bitmap full = Render(s_greenishFill, Colors.Lime, 100f);

            GoldenImageHarness.AssertByteIdentical(baseline, zero, requireVisibleContent: true);

            Rgba original = ReadCenter(baseline);
            Rgba halfSuppressed = ReadCenter(half);
            Rgba fullySuppressed = ReadCenter(full);
            // The readback is linear and premultiplied, so the common alpha factors out of the average.
            float greenLimit = (original.Red + original.Blue) * 0.5f;
            Assert.Multiple(() =>
            {
                Assert.That(original.Alpha, Is.EqualTo(s_greenishFill.A / 255f).Within(Tolerance));
                Assert.That(original.Green, Is.GreaterThan(greenLimit));
                Assert.That(halfSuppressed.Green, Is.EqualTo((original.Green + greenLimit) * 0.5f).Within(Tolerance));
                Assert.That(fullySuppressed.Green, Is.EqualTo(greenLimit).Within(Tolerance));
                Assert.That(fullySuppressed.Red, Is.EqualTo(original.Red).Within(Tolerance));
                Assert.That(fullySuppressed.Blue, Is.EqualTo(original.Blue).Within(Tolerance));
                Assert.That(halfSuppressed.Alpha, Is.EqualTo(original.Alpha));
                Assert.That(fullySuppressed.Alpha, Is.EqualTo(original.Alpha));
            });
        });
    }

    [TestCase(0, 0, 0)]
    [TestCase(128, 128, 128)]
    [TestCase(12, 13, 12)]
    public void KeyWithLowChroma_FullSuppression_PreservesTheKeptColor(byte red, byte green, byte blue)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Color key = Color.FromRgb(red, green, blue);
            using Bitmap baseline = Render(s_greenishFill, key);
            using Bitmap suppressed = Render(s_greenishFill, key, 100f);

            GoldenImageHarness.AssertByteIdentical(baseline, suppressed, requireVisibleContent: true);
        });
    }

    [Test]
    public void BlueKey_LimitsBlueAndPreservesGreen()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var fill = Color.FromArgb(160, 80, 160, 170);
            using Bitmap baseline = Render(fill, Colors.Blue);
            using Bitmap suppressed = Render(fill, Colors.Blue, 100f);

            Rgba original = ReadCenter(baseline);
            Rgba result = ReadCenter(suppressed);
            float blueLimit = (original.Red + original.Green) * 0.5f;
            Assert.Multiple(() =>
            {
                Assert.That(original.Alpha, Is.EqualTo(fill.A / 255f).Within(Tolerance));
                Assert.That(original.Blue, Is.GreaterThan(blueLimit));
                Assert.That(result.Blue, Is.EqualTo(blueLimit).Within(Tolerance));
                Assert.That(result.Green, Is.EqualTo(original.Green).Within(Tolerance));
                Assert.That(result.Red, Is.EqualTo(original.Red).Within(Tolerance));
                Assert.That(result.Alpha, Is.EqualTo(original.Alpha));
            });
        });
    }

    [Test]
    public void FullSuppression_LeavesAKeyedAwayPixelTransparent()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = Render(Colors.Lime, Colors.Lime, 100f);
            Assert.That(ReadCenter(rendered), Is.EqualTo(default(Rgba)));
        });
    }

    private static Bitmap Render(Color fill, Color key, float? spillSuppression = null)
    {
        var effect = new ChromaKey { Color = { Value = key } };
        if (spillSuppression.HasValue)
            effect.SpillSuppression.Value = spillSuppression.Value;

        return RenderEffect(fill, effect);
    }

    private static Bitmap RenderEffect(Color fill, FilterEffect effect)
    {
        var shape = new RectShape
        {
            Width = { Value = s_frame.Width },
            Height = { Value = s_frame.Height },
            Fill = { Value = new SolidColorBrush(fill) },
            FilterEffect = { Value = effect },
        };
        using Drawable.Resource resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
        return GoldenImageHarness.RenderAtScale(resource, s_frame, 1f, clearColor: Colors.Transparent);
    }

    private static Rgba ReadCenter(Bitmap bitmap)
        => ImageMetrics.ReadPixel(bitmap, bitmap.Width / 2, bitmap.Height / 2);
}

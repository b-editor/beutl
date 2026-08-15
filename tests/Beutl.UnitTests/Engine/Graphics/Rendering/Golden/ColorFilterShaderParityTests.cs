using System.Reactive;

using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// Pins the shared color-matrix CurrentPixel stage against the Skia color filter it replaces.
/// </summary>
/// <remarks>
/// <c>SKColorFilter.CreateColorMatrix</c> unpremultiplies without clamping the straight components, multiplies by
/// the matrix, clamps the product to [0, 1], and re-premultiplies. A shader that skipped the unpremultiply or
/// output clamp, or that transposed the matrix wrongly would still look plausible on opaque mid-tone input, so
/// the sweep deliberately includes transparent, semi-transparent, near-zero, out-of-range, and saturating
/// samples. The near-zero alpha band and the non-zero alpha-offset matrix specifically pin the two cases a
/// transparency shortcut inside the stage would get wrong: an alpha offset can make a transparent pixel
/// visible, and a tiny alpha still unpremultiplies into a saturating value rather than into nothing.
/// <para>
/// Parity is bounded, not bit-exact. Skia carries the matrix in <c>half</c> uniforms, so on a backend whose
/// <c>half</c> is real fp16 (Metal through MoltenVK) the reference itself works from coefficients quantized
/// to about 2^-11 relative, while this stage takes them at float precision; a backend that evaluates
/// <c>half</c> at float precision (SwiftShader) makes that quantization a no-op and the two agree bit for
/// bit. Feeding both paths fp16-rounded coefficients collapses the divergence, which is what identifies it.
/// </para>
/// <para>
/// Two bounds hold together because neither one alone covers the sweep. The absolute bound expresses the
/// coefficient quantization, but says nothing about the near-zero alpha band, whose outputs are subnormal
/// and orders of magnitude under it - a stage that blanked that band would pass it. The code-distance bound
/// covers the band, where quantizing a coefficient cannot move a result more than a code or so, and is left
/// off the normal range, where near-cancellation legitimately amplifies the same quantization into tens of
/// codes.
/// </para>
/// </remarks>
[NonParallelizable]
[TestFixture]
public sealed class ColorFilterShaderParityTests
{
    /// <summary>Two fp16 steps at 1.0: the reference's own coefficient precision.</summary>
    private const double MaximumAbsoluteError = 1.0 / 1024;

    /// <summary>The largest half value below the smallest normal, so only subnormal outputs are compared.</summary>
    private const float SubnormalMagnitudeCeiling = 6.103515625e-5f;

    private const int MaximumSubnormalStorageCodeDistance = 2;

    private static readonly Rect s_bounds = new(0, 0, Sweep.Width, Sweep.Height);

    [TestCaseSource(nameof(Amounts))]
    [Category("GpuPassFusionGpu")]
    public void ShaderColorMatrix_MatchesTheSkiaColorFilter(float amount)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            float[] matrix = new float[ColorMatrixShader.SkiaColorMatrixLength];
            ColorMatrix.CreateBrightness(amount, matrix);

            AssertStorageCodeParity(
                $"ColorMatrix amount={amount:R}",
                context => AppendSkiaColorMatrix(context, matrix),
                context => context.Shader(ColorMatrixShader.CurrentPixel(matrix)));
        });
    }

    /// <summary>
    /// A brightness matrix is diagonal with a zero translation column, so it cannot detect a wrongly transposed
    /// uniform or a dropped offset. These matrices are asymmetric and carry offsets, so both would show up.
    /// </summary>
    [TestCaseSource(nameof(StructuredMatrices))]
    [Category("GpuPassFusionGpu")]
    public void ShaderColorMatrix_MatchesTheSkiaColorFilterForAsymmetricAndOffsetMatrices(
        string name,
        float[] matrix)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            AssertStorageCodeParity(
                name,
                context => AppendSkiaColorMatrix(context, matrix),
                context => context.Shader(ColorMatrixShader.CurrentPixel(matrix)));
        });
    }

    /// <summary>
    /// The Brightness effect must record one fusable shader stage and no legacy Skia segment.
    /// </summary>
    [Test]
    public void Brightness_RecordsOneCurrentPixelStageWithoutALegacyBoundary()
    {
        using var context = new FilterEffectContext(s_bounds);

        context.Brightness(0.75f);

        IReadOnlyList<IFEItem> items = context.GetOrderedItems();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items.OfType<IFEItem_Skia>(), Is.Empty);
            Assert.That(items.OfType<IFEItem_Custom>(), Is.Empty);
            Assert.That(context.Bounds, Is.EqualTo(s_bounds));
        }

        var item = (FEItem_Shader)items.Single();
        Assert.That(item.Description.Kind, Is.EqualTo(ShaderDescriptionKind.CurrentPixel));
    }

    [TestCase(0f)]
    [TestCase(0.35f)]
    [TestCase(2f)]
    [Category("GpuPassFusionGpu")]
    public void Saturate_MatchesTheSkiaColorFilterWithinOneStorageCode(float amount)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            float[] matrix = new float[ColorMatrixShader.SkiaColorMatrixLength];
            ColorMatrix.CreateSaturateMatrix(amount, matrix);

            AssertStorageCodeParity(
                $"Saturate amount={amount:R}",
                context => AppendSkiaColorMatrix(context, matrix),
                context => context.Saturate(amount));
        });
    }

    [TestCase(90f)]
    [TestCase(180f)]
    [TestCase(-45f)]
    [Category("GpuPassFusionGpu")]
    public void HueRotate_MatchesTheSkiaColorFilterWithinOneStorageCode(float degrees)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            float[] matrix = new float[ColorMatrixShader.SkiaColorMatrixLength];
            ColorMatrix.CreateHueRotateMatrix(degrees, matrix);

            AssertStorageCodeParity(
                $"HueRotate degrees={degrees:R}",
                context => AppendSkiaColorMatrix(context, matrix),
                context => context.HueRotate(degrees));
        });
    }

    [TestCaseSource(nameof(LightingCases))]
    [Category("GpuPassFusionGpu")]
    public void Lighting_WithNonZeroOffsetsMatchesTheSkiaColorFilterWithinOneStorageCode(
        string name,
        Color multiply,
        Color add)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            float[] matrix = CreateLightingMatrix(multiply, add);

            AssertStorageCodeParity(
                $"Lighting {name} multiply={multiply} add={add}",
                context => AppendSkiaColorMatrix(context, matrix),
                context => context.Lighting(multiply, add));
        });
    }

    [Test]
    public void MigratedColorEffects_RecordOneCurrentPixelStageWithoutALegacyBoundary()
    {
        AssertRecordsOneCurrentPixelStage(context => context.Saturate(2f));
        AssertRecordsOneCurrentPixelStage(context => context.HueRotate(90f));
        AssertRecordsOneCurrentPixelStage(context => context.Lighting(
            Color.FromRgb(128, 200, 64),
            Color.FromRgb(32, 64, 96)));
        AssertRecordsOneCurrentPixelStage(context => context.LumaColor());
        AssertRecordsOneCurrentPixelStage(context => context.HighContrast(
            grayscale: true,
            HighContrastInvertStyle.InvertLightness,
            contrast: 0.6f));
    }

    [TestCase(float.NaN)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1.01f)]
    [TestCase(1.01f)]
    public void HighContrast_InvalidConfigurationRemainsANoOp(float contrast)
    {
        using var context = new FilterEffectContext(s_bounds);

        context.HighContrast(false, HighContrastInvertStyle.NoInvert, contrast);

        Assert.That(context.GetOrderedItems(), Is.Empty);
    }

    [Test]
    public void HighContrast_InvalidInvertStyleRemainsANoOp()
    {
        using var context = new FilterEffectContext(s_bounds);

        context.HighContrast(false, (HighContrastInvertStyle)int.MaxValue, 0.25f);

        Assert.That(context.GetOrderedItems(), Is.Empty);
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void LumaColor_MatchesTheSkiaColorFilterWithinOneStorageCode()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() => AssertStorageCodeParity(
            "LumaColor",
            static context => context.AppendSKColorFilter(
                Unit.Default,
                static (_, _) => SKColorFilter.CreateLumaColor()),
            static context => context.LumaColor()));
    }

    [TestCaseSource(nameof(HighContrastCases))]
    [Category("GpuPassFusionGpu")]
    public void HighContrast_MatchesTheSkiaColorFilterWithinOneStorageCode(
        bool grayscale,
        HighContrastInvertStyle invertStyle,
        float contrast)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() => AssertStorageCodeParity(
            $"HighContrast grayscale={grayscale} invert={invertStyle} contrast={contrast:R}",
            context => context.AppendSKColorFilter(
                Unit.Default,
                (_, _) => SKColorFilter.CreateHighContrast(
                    grayscale,
                    (SKHighContrastConfigInvertStyle)invertStyle,
                    contrast)),
            context => context.HighContrast(grayscale, invertStyle, contrast)));
    }

    /// <summary>
    /// Non-vacuity: the sweep must actually exercise the clamps and the unpremultiply, otherwise a shader that
    /// dropped one of those steps could pass the parity assertion.
    /// </summary>
    [Test]
    public void Sweep_CoversTransparentSemiTransparentAndOutOfRangeSamples()
    {
        Rgba[] samples = Sweep.Samples();
        TestCaseData[] highContrastCases = HighContrastCases().ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(samples.Any(static sample => sample.A == 0f), "the sweep must contain alpha 0.");
            Assert.That(
                samples.Any(static sample => sample.A is > 0.4f and < 0.6f),
                "the sweep must contain alpha 0.5.");
            Assert.That(samples.Any(static sample => sample.A == 1f), "the sweep must contain alpha 1.");
            Assert.That(
                samples.Any(static sample => sample.R > 1f || sample.G > 1f || sample.B > 1f),
                "the sweep must contain premultiplied components above 1.");
            Assert.That(
                samples.Any(static sample => sample.R < 0f || sample.G < 0f || sample.B < 0f),
                "the sweep must contain components below 0.");
            Assert.That(
                samples.Any(static sample => sample.A > 0f && sample.R / sample.A > 1f),
                "the sweep must contain a sample whose unpremultiplied value exceeds 1.");

            // A stage that blanked out every sample below a small alpha threshold would still pass every
            // assertion above, so the sweep has to reach into that band explicitly.
            Assert.That(
                samples.Where(static sample => sample.A > 0f)
                    .Select(static sample => sample.A)
                    .Distinct()
                    .Count(static alpha => alpha <= 1e-4f),
                Is.GreaterThanOrEqualTo(3),
                "the sweep must contain at least three distinct alphas in the 0 < a <= 1e-4 band.");
            Assert.That(
                samples.Any(static sample => sample.A is > 0f and <= 1e-4f && sample.R / sample.A > 1f),
                "the tiny-alpha band must contain a sample that saturates after the unpremultiply.");

            // Non-canonical premultiplied sample: only this one distinguishes Skia's unconditional
            // divide-by-max(a, 1e-4) from a shader that branches on alpha and returns black.
            Assert.That(
                samples.Any(static sample => sample.A == 0f
                    && (sample.R != 0f || sample.G != 0f || sample.B != 0f)),
                "the sweep must contain a sample with alpha 0 and non-zero premultiplied color.");

            // Likewise, the matrix set has to contain a non-zero alpha offset, otherwise the shortcut a
            // transparent pixel would take could never be observed.
            Assert.That(
                StructuredMatrices().Any(static data => ((float[])data.Arguments[1]!)[19] != 0f),
                "the structured matrix set must contain a matrix whose alpha offset is non-zero.");

            foreach (float endpoint in new[] { -1f, 0f, 1f })
            {
                Assert.That(
                    highContrastCases.Select(static data => (float)data.Arguments[2]!),
                    Does.Contain(endpoint),
                    $"the HighContrast cases must contain the contrast endpoint {endpoint:R}.");
            }

            Assert.That(
                highContrastCases.Any(static data => !(bool)data.Arguments[0]!
                    && (HighContrastInvertStyle)data.Arguments[1]!
                        == HighContrastInvertStyle.InvertLightness),
                "the HighContrast cases must exercise rgbToHsl without first converting to grayscale.");
            Assert.That(
                samples.Any(static sample => sample.A > 0f && sample.R == sample.G && sample.R > sample.B),
                "the sweep must contain an R == G > B sample.");
            Assert.That(
                samples.Any(static sample => sample.A > 0f && sample.G == sample.B && sample.G > sample.R),
                "the sweep must contain a G == B > R sample.");
            Assert.That(
                samples.Any(static sample => sample.A > 0f && sample.R == sample.B && sample.R > sample.G),
                "the sweep must contain an R == B > G sample.");
            Assert.That(
                samples.Any(static sample => sample.A > 0f
                    && sample.R == sample.G && sample.G == sample.B),
                "the sweep must contain an R == G == B sample.");
        }
    }

    private static IEnumerable<float> Amounts()
    {
        // Identity, extinguishing, darkening, brightening, saturating, and a sign flip. The large and negative
        // amounts push the product outside [0, 1] where the output clamp decides the result.
        yield return 1f;
        yield return 0f;
        yield return 0.5f;
        yield return 2f;
        yield return 12.5f;
        yield return 1e4f;
        yield return -3f;
    }

    private static IEnumerable<TestCaseData> StructuredMatrices()
    {
        // Asymmetric: every row mixes the channels differently, so a transposed uniform changes the result.
        float[] hueRotate = new float[ColorMatrixShader.SkiaColorMatrixLength];
        ColorMatrix.CreateHueRotateMatrix(50f, hueRotate);
        yield return new TestCaseData("hueRotate50", hueRotate).SetName("Asymmetric_HueRotate");

        float[] saturate = new float[ColorMatrixShader.SkiaColorMatrixLength];
        ColorMatrix.CreateSaturateMatrix(0.35f, saturate);
        yield return new TestCaseData("saturate0.35", saturate).SetName("Asymmetric_Saturate");

        // Carries a non-zero translation column, so a dropped offset uniform changes the result.
        float[] contrast = new float[ColorMatrixShader.SkiaColorMatrixLength];
        ColorMatrix.CreateContrast(35f, contrast);
        yield return new TestCaseData("contrast35", contrast).SetName("Offset_Contrast");

        // Luminance-to-alpha writes only the alpha row, so a transpose would leak it into the color rows.
        float[] luminance = new float[ColorMatrixShader.SkiaColorMatrixLength];
        ColorMatrix.CreateLuminanceToAlphaMatrix(luminance);
        yield return new TestCaseData("luminanceToAlpha", luminance).SetName("Asymmetric_LuminanceToAlpha");

        // None of the ColorMatrix factories writes the alpha offset (slot 19), so these are built directly.
        // A non-zero alpha offset is the case a transparency shortcut inside the stage gets wrong: Skia
        // produces a non-zero alpha from a fully transparent pixel, and the RGB offsets survive the
        // re-premultiply. The shader must reproduce that instead of short-circuiting to zero.
        yield return new TestCaseData(
                "alphaOffsetOnly",
                new float[]
                {
                    1f, 0f, 0f, 0f, 0f,
                    0f, 1f, 0f, 0f, 0f,
                    0f, 0f, 1f, 0f, 0f,
                    0f, 0f, 0f, 1f, 0.5f,
                })
            .SetName("Offset_AlphaOffsetOnly");

        // Alpha offset plus RGB offsets and an asymmetric multiplier: a transparent pixel becomes a visibly
        // colored one, so dropping either offset or transposing the multiplier all diverge here.
        yield return new TestCaseData(
                "alphaAndColorOffset",
                new float[]
                {
                    0.6f, 0.2f, 0.1f, 0f, 0.25f,
                    0.1f, 0.7f, 0.2f, 0f, 0.125f,
                    0.3f, 0.1f, 0.5f, 0f, 0.0625f,
                    0f, 0f, 0f, 0.5f, 0.375f,
                })
            .SetName("Offset_AlphaAndColorOffset");

        // A negative alpha offset drives the transformed alpha below zero for part of the sweep, where the
        // output clamp - not the shortcut - has to decide the result.
        yield return new TestCaseData(
                "negativeAlphaOffset",
                new float[]
                {
                    1f, 0f, 0f, 0f, 0.5f,
                    0f, 1f, 0f, 0f, 0f,
                    0f, 0f, 1f, 0f, 0f,
                    0f, 0f, 0f, 1f, -0.25f,
                })
            .SetName("Offset_NegativeAlphaOffset");
    }

    private static IEnumerable<TestCaseData> LightingCases()
    {
        // Both the diagonal multiplier and the translation column are active. The two cases use different
        // channels so an accidentally dropped or reordered offset cannot pass vacuously.
        yield return new TestCaseData(
                "mixedChannels",
                Color.FromRgb(128, 200, 64),
                Color.FromRgb(32, 64, 96))
            .SetName("Lighting_MixedMultipliersAndOffsets");
        yield return new TestCaseData(
                "strongOffset",
                Color.FromRgb(224, 96, 160),
                Color.FromRgb(80, 16, 48))
            .SetName("Lighting_StrongNonZeroOffset");
    }

    private static IEnumerable<TestCaseData> HighContrastCases()
    {
        foreach (bool grayscale in new[] { false, true })
        {
            yield return new TestCaseData(grayscale, HighContrastInvertStyle.NoInvert, 0.35f)
                .SetName($"HighContrast_Grayscale{grayscale}_NoInvert");
            yield return new TestCaseData(grayscale, HighContrastInvertStyle.InvertBrightness, -0.4f)
                .SetName($"HighContrast_Grayscale{grayscale}_InvertBrightness");
            yield return new TestCaseData(grayscale, HighContrastInvertStyle.InvertLightness, 0.6f)
                .SetName($"HighContrast_Grayscale{grayscale}_InvertLightness");
            yield return new TestCaseData(grayscale, HighContrastInvertStyle.NoInvert, -1f)
                .SetName($"HighContrast_Grayscale{grayscale}_ContrastMinimum");
            yield return new TestCaseData(grayscale, HighContrastInvertStyle.NoInvert, 0f)
                .SetName($"HighContrast_Grayscale{grayscale}_ContrastNeutral");
            yield return new TestCaseData(grayscale, HighContrastInvertStyle.NoInvert, 1f)
                .SetName($"HighContrast_Grayscale{grayscale}_ContrastMaximum");
        }
    }

    private static float[] CreateLightingMatrix(Color multiply, Color add)
    {
        var mulLinear = multiply.ToLinear();
        var addLinear = add.ToLinear();
        var matrix = new float[ColorMatrixShader.SkiaColorMatrixLength];
        matrix[0] = mulLinear.X;
        matrix[6] = mulLinear.Y;
        matrix[12] = mulLinear.Z;
        matrix[18] = 1;
        matrix[4] = addLinear.X;
        matrix[9] = addLinear.Y;
        matrix[14] = addLinear.Z;
        return matrix;
    }

    private static void AssertStorageCodeParity(
        string label,
        Action<FilterEffectContext> appendSkia,
        Action<FilterEffectContext> appendShader)
    {
        using Bitmap skia = Execute(appendSkia);
        using Bitmap shader = Execute(appendShader);

        RgbaMaximumError error = ImageMetrics.MaximumAbsoluteErrorPerChannel(skia, shader);
        RgbaMaximumError codes = ImageMetrics.MaximumStorageCodeDistancePerChannel(
            skia,
            shader,
            SubnormalMagnitudeCeiling);
        TestContext.WriteLine(
            $"{label} max per-channel error r={error.Red:R} g={error.Green:R} "
            + $"b={error.Blue:R} a={error.Alpha:R}; subnormal codes r={codes.Red} g={codes.Green} "
            + $"b={codes.Blue} a={codes.Alpha}");

        Assert.That(
            ImageMetrics.FirstNonFinite(("skia", skia), ("shader", shader)),
            Is.Null,
            $"Both {label} paths must produce finite RGBA16F values.");
        Assert.That(
            error.Maximum,
            Is.LessThanOrEqualTo(MaximumAbsoluteError),
            $"The CurrentPixel {label} path must reproduce the Skia color filter to within the precision "
            + $"Skia itself carries; measured max per-channel error r={error.Red:R} g={error.Green:R} "
            + $"b={error.Blue:R} a={error.Alpha:R}.");
        Assert.That(
            codes.Maximum,
            Is.LessThanOrEqualTo(MaximumSubnormalStorageCodeDistance),
            $"The CurrentPixel {label} path must stay within {MaximumSubnormalStorageCodeDistance} RgbaF16 "
            + "codes of the Skia color filter across the subnormal band, where the absolute bound above is "
            + $"too coarse to see anything; measured max per-channel distance r={codes.Red} g={codes.Green} "
            + $"b={codes.Blue} a={codes.Alpha}.");
    }

    private static void AssertRecordsOneCurrentPixelStage(Action<FilterEffectContext> record)
    {
        using var context = new FilterEffectContext(s_bounds);
        record(context);

        IReadOnlyList<IFEItem> items = context.GetOrderedItems();
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items.OfType<IFEItem_Skia>(), Is.Empty);
            Assert.That(items.OfType<IFEItem_Custom>(), Is.Empty);
            Assert.That(items.Single(), Is.TypeOf<FEItem_Shader>());
            Assert.That(((FEItem_Shader)items.Single()).Description.Kind,
                Is.EqualTo(ShaderDescriptionKind.CurrentPixel));
        });
    }

    private static void AppendSkiaColorMatrix(FilterEffectContext context, float[] matrix)
    {
        float[] copy = matrix.ToArray();
        context.AppendSKColorFilter(Unit.Default, (_, _) => SKColorFilter.CreateColorMatrix(copy));
    }

    private static Bitmap Execute(Action<FilterEffectContext> record)
    {
        using RenderTarget backing = RenderTarget.Create(Sweep.Width, Sweep.Height)
            ?? throw new InvalidOperationException("The color-matrix parity target could not be allocated.");
        Sweep.Fill(backing);

        using var targets = new EffectTargets
        {
            new EffectTarget(backing, s_bounds, EffectiveScale.At(1)),
        };
        using var context = new FilterEffectContext(s_bounds);
        record(context);

        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Delivery,
            RenderRequestPurpose.Frame,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);

        activator.Apply(context);
        activator.Flush(false);

        RenderTarget result = targets.Single().RenderTarget
            ?? throw new InvalidOperationException("The color-matrix stage produced no render target.");
        return result.Snapshot();
    }

    /// <summary>The shared premultiplied linear RGBA16F input sweep.</summary>
    private static class Sweep
    {
        // The last three rows are the near-zero band: Skia still unpremultiplies there, so the divide
        // produces a huge value that the output clamp pulls back to 1 before the re-premultiply. A stage
        // that treated the band as transparent would return zero instead.
        private static readonly float[] s_alphas = [0f, 0.5f, 1f, 0.25f, 1e-5f, 5e-5f, 1e-4f];

        private static readonly float[] s_straightComponents =
            [0f, 0.25f, 0.5f, 0.75f, 1f, 1.5f, 4f, -0.5f];

        private static readonly (float R, float G, float B)[] s_tieStraightColors =
        [
            (0.75f, 0.75f, 0.25f),
            (0.25f, 0.75f, 0.75f),
            (0.75f, 0.25f, 0.75f),
            (0.5f, 0.5f, 0.5f),
        ];

        public static int Width => s_straightComponents.Length + s_tieStraightColors.Length;

        public static int Height => s_alphas.Length;

        /// <summary>Returns the premultiplied samples written by <see cref="Fill"/>, in row-major order.</summary>
        public static Rgba[] Samples()
        {
            var result = new Rgba[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    result[(y * Width) + x] = Sample(x, y);
            }

            return result;
        }

        public static void Fill(RenderTarget target)
        {
            var info = new SKImageInfo(
                Width,
                Height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear());
            var pixels = new ushort[Width * Height * 4];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Rgba sample = Sample(x, y);
                    int offset = ((y * Width) + x) * 4;
                    pixels[offset] = BitConverter.HalfToUInt16Bits((Half)sample.R);
                    pixels[offset + 1] = BitConverter.HalfToUInt16Bits((Half)sample.G);
                    pixels[offset + 2] = BitConverter.HalfToUInt16Bits((Half)sample.B);
                    pixels[offset + 3] = BitConverter.HalfToUInt16Bits((Half)sample.A);
                }
            }

            unsafe
            {
                fixed (ushort* buffer = pixels)
                {
                    using SKImage image = SKImage.FromPixelCopy(info, (IntPtr)buffer, info.RowBytes);
                    target.Value.Canvas.Clear();
                    target.Value.Canvas.DrawImage(image, 0, 0);
                    target.Value.Canvas.Flush();
                }
            }
        }

        // The original columns use distinct per-channel straight values so a wrongly transposed matrix cannot
        // cancel out. The added columns pin every maximum-value tie branch in rgbToHsl.
        private static Rgba Sample(int x, int y)
        {
            float alpha = s_alphas[y];
            float red;
            float green;
            float blue;
            if (x < s_straightComponents.Length)
            {
                red = s_straightComponents[x];
                green = s_straightComponents[(x + 3) % s_straightComponents.Length];
                blue = s_straightComponents[(x + 5) % s_straightComponents.Length];
            }
            else
            {
                (red, green, blue) = s_tieStraightColors[x - s_straightComponents.Length];
            }

            // Non-canonical premultiplied input: alpha 0 with non-zero RGB. A shader that special-cases
            // alpha == 0 to black diverges from Skia here, because Skia's unpremultiply divides by
            // max(a, 1e-4) unconditionally rather than branching on alpha.
            if (alpha == 0f && x == 0)
                return new Rgba(1e-3f, 5e-4f, 2.5e-4f, 0f);

            return new Rgba(red * alpha, green * alpha, blue * alpha, alpha);
        }
    }

    private readonly record struct Rgba(float R, float G, float B, float A);
}

using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public class LargeSigmaBlurCoverageTests
{
    private static readonly PixelSize s_frame = new(600, 400);
    private static readonly PixelSize s_largeSigmaFrame = new(960, 540);
    private const float Sigma = 250f;
    private const float OutputScale = 4f;

    private static readonly float[] s_sigmas = [130, 250, 480, 500, 512, 520, 600];
    private static readonly float[] s_outputScales = [1, 1.02f, 1.03f, 2, 4];
    private static readonly float[] s_shadowSigmas = [460, 500, 540, 700, 2000];
    private static readonly int[] s_smallSourceExtents = [25, 50, 100, 200];
    private static readonly float[] s_smallSourceSigmas = [127, 128, 250, 500];

    // The blur cap-boundary matrix runs in the regular unfiltered test gate. Other dense
    // backend-characterization sweeps remain Explicit and document their dedicated filters.
    public static IEnumerable<TestCaseData> LargeSigmaCases()
    {
        foreach (float sigma in s_sigmas)
        {
            foreach (float outputScale in s_outputScales)
            {
                yield return new TestCaseData(sigma, outputScale)
                    .SetName(
                        $"{nameof(Blur_CenterCoverageMatchesGaussianModelAcrossCapBoundary)}_Sigma{sigma}_Scale{outputScale}");
            }
        }

        foreach (float sigma in new[] { 127f, 128f })
        {
            foreach (float outputScale in new[] { 1f, 1.02f, 1.05f })
            {
                yield return new TestCaseData(sigma, outputScale)
                    .SetName(
                        $"{nameof(Blur_CenterCoverageMatchesGaussianModelAcrossCapBoundary)}_DeviceSigmaThreshold_Sigma{sigma}_Scale{outputScale}");
            }
        }
    }

    public static IEnumerable<TestCaseData> SmallSourceBlurCases()
    {
        foreach (int sourceExtent in s_smallSourceExtents)
        {
            foreach (float sigma in s_smallSourceSigmas)
            {
                yield return new TestCaseData(sourceExtent, sigma)
                    .SetName($"Blur_SmallSourceGaussianResponse_Extent{sourceExtent}_Sigma{sigma}");
            }
        }

        yield return new TestCaseData(200, 2000f)
            .SetName("Blur_SmallSourceGaussianResponse_Extent200_Sigma2000");
    }

    [Test]
    public void HighOutputScale_BlurCenterCoverageMatchesGaussianModel()
    {
        AssertHighOutputScaleCoverage("Blur");
    }

    [Test]
    [Explicit(
        "Orchestrator gate: dotnet test tests/Beutl.UnitTests -f net10.0 "
        + "--filter \"FullyQualifiedName~LargeSigmaBlurCoverageTests.HighOutputScale_DropShadowCenterCoverageMatchesGaussianModel\"")]
    public void HighOutputScale_DropShadowCenterCoverageMatchesGaussianModel()
    {
        AssertHighOutputScaleCoverage("DropShadow");
    }

    private static void AssertHighOutputScaleCoverage(string effectName)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource drawable = CreateDrawable(effectName);
            using Bitmap bitmap = GoldenImageHarness.RenderAtScale(
                drawable,
                s_frame,
                OutputScale,
                clearColor: Colors.Transparent);

            float actual = ReadAlpha(bitmap, bitmap.Width / 2, bitmap.Height / 2);
            double expected = GaussianIntervalCoverage(s_frame.Width, Sigma)
                              * GaussianIntervalCoverage(s_frame.Height, Sigma);
            TestContext.WriteLine(
                $"{effectName}: center alpha={actual:F6}, Gaussian model={expected:F6}, delta={Math.Abs(actual - expected):F6}");

            Assert.That(
                actual,
                Is.EqualTo(expected).Within(0.03),
                "A large logical blur must remain resolution-independent when the output scale exceeds Skia's safe device sigma.");
        });
    }

    [TestCaseSource(nameof(LargeSigmaCases))]
    public void Blur_CenterCoverageMatchesGaussianModelAcrossCapBoundary(
        float sigma,
        float outputScale)
    {
        AssertBlurCoverage(sigma, outputScale);
    }

    [Test]
    public void Blur_RepresentativeCapBoundaryCoverageMatchesGaussianModel()
    {
        AssertBlurCoverage(sigma: 500, outputScale: 1);
    }

    [TestCase(127f)]
    [TestCase(128f)]
    public void Blur_UnitScaleSinglePassMultipassSeamMatchesGaussianModel(float sigma)
    {
        AssertBlurCoverage(sigma, outputScale: 1);
    }

    [TestCaseSource(nameof(s_shadowSigmas))]
    [Explicit(
        "Orchestrator gate: dotnet test tests/Beutl.UnitTests -f net10.0 "
        + "--filter \"FullyQualifiedName~LargeSigmaBlurCoverageTests.DropShadowOnly_CenterCoverageMatchesGaussianModel\"")]
    public void DropShadowOnly_CenterCoverageMatchesGaussianModel(float sigma)
    {
        AssertDropShadowCoverage(sigma);
    }

    [Test]
    public void DropShadowOnly_RepresentativeDensityCoverageMatchesGaussianModel()
    {
        AssertDropShadowCoverage(sigma: 500);
    }

    [Test]
    [Explicit(
        "Orchestrator gate: dotnet test tests/Beutl.UnitTests -f net10.0 "
        + "--filter \"FullyQualifiedName~LargeSigmaBlurCoverageTests.DropShadowOnly_CenterCoverageIsMonotoneAcrossLargeSigmas\"")]
    public void DropShadowOnly_CenterCoverageIsMonotoneAcrossLargeSigmas()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            float previous = float.PositiveInfinity;
            foreach (float sigma in new[] { 400f, 460f, 470f, 490f, 500f, 540f, 700f })
            {
                using Drawable.Resource drawable = CreateSquareDropShadowOnly(200, sigma);
                using Bitmap bitmap = GoldenImageHarness.RenderAtScale(
                    drawable,
                    s_largeSigmaFrame,
                    1);
                float actual = ReadRed(bitmap, bitmap.Width / 2, bitmap.Height / 2);

                Assert.That(
                    actual,
                    Is.LessThanOrEqualTo(previous + (1f / 255f)),
                    $"Gaussian centre coverage increased at sigma {sigma}.");
                previous = actual;
            }
        });
    }

    [TestCaseSource(nameof(SmallSourceBlurCases))]
    [Explicit(
        "Orchestrator gate: dotnet test tests/Beutl.UnitTests -f net10.0 "
        + "--filter \"FullyQualifiedName~LargeSigmaBlurCoverageTests.Blur_SmallSourceCenterCoverageMatchesGaussianModel\"")]
    public void Blur_SmallSourceCenterCoverageMatchesGaussianModel(
        int sourceExtent,
        float sigma)
    {
        AssertSmallSourceBlurCoverage(sourceExtent, sigma);
    }

    [Test]
    public void Blur_RepresentativeSmallSourceCoverageMatchesGaussianModel()
    {
        AssertSmallSourceBlurCoverage(sourceExtent: 50, sigma: 500);
    }

    [Test]
    public void DropShadow_OpaqueLargeSigmaShadowNeverResamplesSharpSubject()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource filtered = CreateSharpSquare(new DropShadow
            {
                Sigma = { CurrentValue = new Size(600, 600) },
                Color = { CurrentValue = Colors.Red },
                Position = { CurrentValue = default },
            });
            using Bitmap actual = GoldenImageHarness.RenderAtScale(
                filtered,
                s_largeSigmaFrame,
                1,
                clearColor: Colors.Transparent);

            int y = actual.Height / 2;
            int subjectLeft = (actual.Width - 200) / 2;
            float outsideGreen = ReadGreen(actual, subjectLeft - 1, y);
            float insideGreen = ReadGreen(actual, subjectLeft, y);
            Assert.Multiple(() =>
            {
                Assert.That(
                    outsideGreen,
                    Is.LessThanOrEqualTo(1f / 255f),
                    "The opaque red shadow contributes no green outside the subject.");
                Assert.That(
                    insideGreen,
                    Is.GreaterThanOrEqualTo(254f / 255f),
                    "The white subject edge must remain a one-pixel step over the low-density shadow.");
            });
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DropShadow_FollowedByNonlinearColorStageConsumesMergedComposite(bool shadowOnly)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var position = new Point(24, 18);
            var sigma = new Size(8, 8);
            var color = new Color(190, 220, 36, 18);
            using Drawable.Resource actual = CreateDropShadowGammaChain(
                new DropShadow
                {
                    Position = { CurrentValue = position },
                    Sigma = { CurrentValue = sigma },
                    Color = { CurrentValue = color },
                    ShadowOnly = { CurrentValue = shadowOnly },
                });
            using Drawable.Resource expected = CreateDropShadowGammaChain(
                new MergedDropShadowReferenceEffect(position, sigma, color, shadowOnly));

            using Bitmap actualBitmap = GoldenImageHarness.RenderAtScale(
                actual,
                s_largeSigmaFrame,
                1,
                clearColor: Colors.Transparent);
            using Bitmap expectedBitmap = GoldenImageHarness.RenderAtScale(
                expected,
                s_largeSigmaFrame,
                1,
                clearColor: Colors.Transparent);

            GoldenImageHarness.AssertByteIdentical(expectedBitmap, actualBitmap);
        });
    }

    private static Drawable.Resource CreateDrawable(string effectName)
    {
        FilterEffect effect = effectName switch
        {
            "Blur" => new Blur
            {
                Sigma = { CurrentValue = new Size(Sigma, Sigma) }
            },
            "DropShadow" => new DropShadow
            {
                Sigma = { CurrentValue = new Size(Sigma, Sigma) },
                Position = { CurrentValue = default },
                Color = { CurrentValue = Colors.White },
                ShadowOnly = { CurrentValue = true }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(effectName), effectName, null)
        };
        var shape = new RectShape
        {
            Width = { CurrentValue = s_frame.Width },
            Height = { CurrentValue = s_frame.Height },
            Fill = { CurrentValue = Brushes.White },
            AlignmentX = { CurrentValue = AlignmentX.Center },
            AlignmentY = { CurrentValue = AlignmentY.Center },
            FilterEffect = { CurrentValue = effect }
        };
        return shape.ToResource(CompositionContext.Default);
    }

    private static void AssertBlurCoverage(float sigma, float outputScale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource drawable = CreateSquareBlur(sigma);
            using Bitmap bitmap = GoldenImageHarness.RenderAtScale(
                drawable,
                s_largeSigmaFrame,
                outputScale);

            float actualLinear = ReadRed(bitmap, bitmap.Width / 2, bitmap.Height / 2);
            double expectedLinear = GaussianIntervalCoverage(200, sigma)
                                    * GaussianIntervalCoverage(200, sigma);
            float actualSrgb = Color.LinearToSrgb(actualLinear);
            double expectedSrgb = Color.LinearToSrgb((float)expectedLinear);
            TestContext.WriteLine(
                $"sigma={sigma}, scale={outputScale}: center={actualSrgb * 255:F3}, "
                + $"Gaussian model={expectedSrgb * 255:F3}, delta={Math.Abs(actualSrgb - expectedSrgb) * 255:F3}");

            // Three encoded levels separate native approximation noise from the large discontinuity
            // caused by exceeding the backend's reliable device-sigma range.
            Assert.That(
                actualSrgb,
                Is.EqualTo(expectedSrgb).Within(3f / 255f),
                "Large Gaussian blur coverage must be continuous and resolution-independent.");
        });
    }

    private static void AssertDropShadowCoverage(float sigma)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource drawable = CreateSquareDropShadowOnly(200, sigma);
            using Bitmap bitmap = GoldenImageHarness.RenderAtScale(
                drawable,
                s_largeSigmaFrame,
                1);

            float actualLinear = ReadRed(bitmap, bitmap.Width / 2, bitmap.Height / 2);
            double expectedLinear = Math.Pow(GaussianIntervalCoverage(200, sigma), 2);
            float actualSrgb = Color.LinearToSrgb(actualLinear);
            float expectedSrgb = Color.LinearToSrgb((float)expectedLinear);
            TestContext.WriteLine(
                $"DropShadow sigma={sigma}: center={actualSrgb * 255:F3}, "
                + $"Gaussian model={expectedSrgb * 255:F3}");

            Assert.That(
                actualSrgb,
                Is.EqualTo(expectedSrgb).Within(3f / 255f),
                "ShadowOnly must use the same stable Gaussian response as Blur.");
        });
    }

    private static void AssertSmallSourceBlurCoverage(int sourceExtent, float sigma)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource drawable = CreateSquareBlur(sourceExtent, sigma);
            using Bitmap bitmap = GoldenImageHarness.RenderAtScale(
                drawable,
                s_largeSigmaFrame,
                1);

            float actualLinear = ReadRed(bitmap, bitmap.Width / 2, bitmap.Height / 2);
            double expectedLinear = Math.Pow(
                GaussianIntervalCoverage(sourceExtent, sigma),
                2);
            float actualSrgb = Color.LinearToSrgb(actualLinear);
            float expectedSrgb = Color.LinearToSrgb((float)expectedLinear);
            double ratio = actualLinear / expectedLinear;
            TestContext.WriteLine(
                $"extent={sourceExtent}, sigma={sigma}: center={actualSrgb * 255:F3}, "
                + $"Gaussian model={expectedSrgb * 255:F3}, linear ratio={ratio:F4}");

            Assert.Multiple(() =>
            {
                Assert.That(
                    actualSrgb,
                    Is.EqualTo(expectedSrgb).Within(3f / 255f),
                    "Encoded centre coverage must match the analytic Gaussian.");
                Assert.That(
                    ratio,
                    Is.InRange(0.85, 1.15),
                    "Linear centre coverage must not resonate with the reduced source footprint.");
            });
        });
    }

    private static Drawable.Resource CreateSquareBlur(float sigma)
    {
        return CreateSquareBlur(200, sigma);
    }

    private static Drawable.Resource CreateSquareBlur(float extent, float sigma)
    {
        return CreateSharpSquare(extent, new Blur
        {
            Sigma = { CurrentValue = new Size(sigma, sigma) },
        });
    }

    private static Drawable.Resource CreateSharpSquare(FilterEffect? effect)
        => CreateSharpSquare(200, effect);

    private static Drawable.Resource CreateSharpSquare(float extent, FilterEffect? effect)
    {
        var shape = new RectShape
        {
            Width = { CurrentValue = extent },
            Height = { CurrentValue = extent },
            Fill = { CurrentValue = Brushes.White },
            AlignmentX = { CurrentValue = AlignmentX.Center },
            AlignmentY = { CurrentValue = AlignmentY.Center },
            FilterEffect = { CurrentValue = effect },
        };
        return shape.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource CreateSquareDropShadowOnly(float extent, float sigma)
    {
        return CreateSharpSquare(extent, new DropShadow
        {
            Sigma = { CurrentValue = new Size(sigma, sigma) },
            Position = { CurrentValue = default },
            Color = { CurrentValue = Colors.White },
            ShadowOnly = { CurrentValue = true },
        });
    }

    private static Drawable.Resource CreateDropShadowGammaChain(FilterEffect shadow)
    {
        var group = new FilterEffectGroup
        {
            Children =
            {
                shadow,
                new Gamma
                {
                    Amount = { CurrentValue = 180 },
                    Strength = { CurrentValue = 100 },
                },
            },
        };
        var shape = new RectShape
        {
            Width = { CurrentValue = 200 },
            Height = { CurrentValue = 140 },
            Fill = { CurrentValue = new SolidColorBrush(new Color(210, 45, 120, 220)) },
            AlignmentX = { CurrentValue = AlignmentX.Center },
            AlignmentY = { CurrentValue = AlignmentY.Center },
            FilterEffect = { CurrentValue = group },
        };
        return shape.ToResource(CompositionContext.Default);
    }

    private static double GaussianIntervalCoverage(double extent, double sigma)
    {
        return ErrorFunction(extent / (2 * Math.Sqrt(2) * sigma));
    }

    private static double ErrorFunction(double value)
    {
        const double p = 0.3275911;
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;

        double sign = Math.Sign(value);
        double x = Math.Abs(value);
        double t = 1 / (1 + p * x);
        double approximation = 1
                               - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1)
                               * t
                               * Math.Exp(-x * x);
        return sign * approximation;
    }

    private static float ReadAlpha(Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int offset = ((y * bitmap.Width) + x) * 4;
        return (float)BitConverter.UInt16BitsToHalf(pixels[offset + 3]);
    }

    private static float ReadRed(Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int offset = ((y * bitmap.Width) + x) * 4;
        return (float)BitConverter.UInt16BitsToHalf(pixels[offset]);
    }

    private static float ReadGreen(Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int offset = ((y * bitmap.Width) + x) * 4;
        return (float)BitConverter.UInt16BitsToHalf(pixels[offset + 1]);
    }
}

internal sealed partial class MergedDropShadowReferenceEffect(
    Point position,
    Size sigma,
    Color color,
    bool shadowOnly) : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        context.AppendSkiaFilter(
            data: (position, sigma, color, shadowOnly),
            factory: static (data, input, _) => data.shadowOnly
                ? SKImageFilter.CreateDropShadowOnly(
                    data.position.X,
                    data.position.Y,
                    data.sigma.Width,
                    data.sigma.Height,
                    data.color.ToSKColor(),
                    input)
                : SKImageFilter.CreateDropShadow(
                    data.position.X,
                    data.position.Y,
                    data.sigma.Width,
                    data.sigma.Height,
                    data.color.ToSKColor(),
                    input),
            transformBounds: static (data, bounds) => bounds
                .Translate(data.position)
                .Inflate(new Thickness(data.sigma.Width * 3, data.sigma.Height * 3))
                .Union(data.shadowOnly ? Rect.Empty : bounds));
    }

    public new sealed partial class Resource : FilterEffect.Resource;
}

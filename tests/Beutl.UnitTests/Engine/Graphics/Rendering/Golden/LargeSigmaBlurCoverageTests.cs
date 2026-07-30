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

    public static IEnumerable<TestCaseData> LargeSigmaCases()
    {
        foreach (float sigma in s_sigmas)
        {
            foreach (float outputScale in s_outputScales)
            {
                yield return new TestCaseData(sigma, outputScale)
                    .SetName($"Blur_CenterCoverageMatchesGaussianModel_Sigma{sigma}_Scale{outputScale}");
            }
        }

        foreach (float sigma in new[] { 127f, 128f })
        {
            foreach (float outputScale in new[] { 1f, 1.02f, 1.05f })
            {
                yield return new TestCaseData(sigma, outputScale)
                    .SetName($"Blur_DeviceSigmaThreshold_Sigma{sigma}_Scale{outputScale}");
            }
        }
    }

    [TestCase("Blur")]
    [TestCase("DropShadow")]
    public void HighOutputScale_CenterCoverageMatchesGaussianModel(string effectName)
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

    [TestCase(600f)]
    [TestCase(2000f)]
    public void DropShadow_TransparentShadowNeverResamplesSharpSubject(float sigma)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource control = CreateSharpSquare(effect: null);
            using Drawable.Resource filtered = CreateSharpSquare(new DropShadow
            {
                Sigma = { CurrentValue = new Size(sigma, sigma) },
                Color = { CurrentValue = Colors.Transparent },
                Position = { CurrentValue = default },
            });
            using Bitmap expected = GoldenImageHarness.RenderAtScale(
                control,
                s_largeSigmaFrame,
                1,
                clearColor: Colors.Transparent);
            using Bitmap actual = GoldenImageHarness.RenderAtScale(
                filtered,
                s_largeSigmaFrame,
                1,
                clearColor: Colors.Transparent);

            GoldenImageHarness.AssertByteIdentical(expected, actual);
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

    private static Drawable.Resource CreateSquareBlur(float sigma)
    {
        return CreateSharpSquare(new Blur
        {
            Sigma = { CurrentValue = new Size(sigma, sigma) },
        });
    }

    private static Drawable.Resource CreateSharpSquare(FilterEffect? effect)
    {
        var shape = new RectShape
        {
            Width = { CurrentValue = 200 },
            Height = { CurrentValue = 200 },
            Fill = { CurrentValue = Brushes.White },
            AlignmentX = { CurrentValue = AlignmentX.Center },
            AlignmentY = { CurrentValue = AlignmentY.Center },
            FilterEffect = { CurrentValue = effect },
        };
        return shape.ToResource(CompositionContext.Default);
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

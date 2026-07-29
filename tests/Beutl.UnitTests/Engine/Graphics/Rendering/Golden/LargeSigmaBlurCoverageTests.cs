using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public class LargeSigmaBlurCoverageTests
{
    private static readonly PixelSize s_frame = new(600, 400);
    private const float Sigma = 250f;
    private const float OutputScale = 4f;

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
}

using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class WholeSourceFractionalScaleTests
{
    private const string OutsideSourceShader = """
        uniform shader src;
        uniform float2 iResolution;
        half4 main(float2 c) { return src.eval(c + iResolution * 1.5); }
        """;

    [Test]
    public void OutsideSourceSampling_PreservesCoverageFractionAcrossOutputScales()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            float[] scales = [0.5f, 0.75f, 1f, 1.5f, 2f];
            var coverageByScale = new Dictionary<float, (long NonTransparent, long Pixels)>();

            foreach (float scale in scales)
            {
                using Bitmap bitmap = Render(scale);
                coverageByScale.Add(
                    scale,
                    (CountNonTransparentPixels(bitmap), (long)bitmap.Width * bitmap.Height));
            }

            foreach ((float scale, (long coverage, long pixels)) in coverageByScale)
                TestContext.WriteLine($"scale {scale}: {coverage} / {pixels}");

            (long referenceCoverage, long referencePixels) = coverageByScale[1f];
            Assert.That(referenceCoverage, Is.GreaterThan(0),
                "the reference scale must exercise the source shader's Clamp edge");
            Assert.Multiple(() =>
            {
                foreach ((float scale, (long coverage, long pixels)) in coverageByScale)
                {
                    Assert.That(
                        coverage * referencePixels,
                        Is.EqualTo(referenceCoverage * pixels),
                        $"scale {scale} must preserve the non-transparent coverage fraction");
                }
            });
        });
    }

    private static Bitmap Render(float outputScale)
    {
        var effect = new SKSLScriptEffect();
        effect.Script.Value = OutsideSourceShader;

        var rectangle = new RectShape();
        rectangle.Width.Value = 120;
        rectangle.Height.Value = 80;
        rectangle.Fill.Value = new SolidColorBrush(Colors.OrangeRed);
        rectangle.FilterEffect.Value = effect;

        return SceneRenderHarness.RenderSingle(
            rectangle,
            new PixelSize(256, 144),
            outputScale,
            outputScale * 2f,
            TimeSpan.Zero);
    }

    private static long CountNonTransparentPixels(Bitmap bitmap)
    {
        long count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y)[..(bitmap.Width * 4)];
            for (int alpha = 3; alpha < row.Length; alpha += 4)
            {
                if (row[alpha] != 0)
                    count++;
            }
        }

        return count;
    }
}

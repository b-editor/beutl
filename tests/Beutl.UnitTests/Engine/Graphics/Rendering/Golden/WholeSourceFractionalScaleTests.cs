using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
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
        effect.Script.CurrentValue = OutsideSourceShader;

        var rectangle = new RectShape();
        rectangle.Width.CurrentValue = 120;
        rectangle.Height.CurrentValue = 80;
        rectangle.Fill.CurrentValue = new SolidColorBrush(Colors.OrangeRed);
        rectangle.FilterEffect.CurrentValue = effect;

        var scene = new Scene(256, 144, "whole-source-fractional-scale")
        {
            Uri = new Uri("file:///whole-source-fractional-scale/scene"),
        };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            ZIndex = 0,
            IsEnabled = true,
            Uri = new Uri("file:///whole-source-fractional-scale/element"),
        };
        element.AddObject(rectangle);
        scene.Children.Add(element);

        using var renderer = new SceneRenderer(scene, outputScale, false, outputScale * 2f)
        {
            CacheOptions = RenderCacheOptions.Disabled,
        };
        renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.Zero));
        return renderer.Snapshot();
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

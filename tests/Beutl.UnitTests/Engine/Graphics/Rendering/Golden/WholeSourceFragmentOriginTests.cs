using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// A whole-source stage is asked for a strict subset of its output whenever the content overhangs the frame.
// Its coord must still span the complete output, otherwise `coord / iResolution` stops being a normalized
// coordinate and every absolute anchor - a mirror axis, a tile origin - moves with the clip.
[NonParallelizable]
[TestFixture]
public sealed class WholeSourceFragmentOriginTests
{
    private const int Overhang = 64;
    private const int ContentExtent = 224;
    private const int ClippedExtent = ContentExtent - Overhang;

    // Both renders resolve the same texels, so the residual is fp16 storage rounding (~5e-4 relative near 1)
    // rather than resampling.
    private const double CropInvarianceTolerance = 0.002;

    private const string CoordinateProbeShader = """
        uniform shader src;
        uniform float2 iResolution;

        half4 main(float2 coord) {
            half alpha = src.eval(coord).a;
            float2 uv = coord / iResolution;
            return half4(half2(uv), 0.0, 1.0) * alpha;
        }
        """;

    private const string IdentityShader = """
        uniform shader src;
        uniform float2 iResolution;

        half4 main(float2 coord) {
            return src.eval(min(coord, iResolution));
        }
        """;

    private const string HorizontalFlipShader = """
        uniform shader src;
        uniform float2 iResolution;

        half4 main(float2 coord) {
            return src.eval(float2(iResolution.x - coord.x, coord.y));
        }
        """;

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    [Category("GpuPassFusionGpu")]
    public void FragmentCoordinate_SpansTheCompleteOutput_WhenTheRequiredRegionIsAStrictSubset(bool fused)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap complete = RenderComplete(CoordinateProbeShader, fused);
            using Bitmap clipped = RenderClipped(CoordinateProbeShader, fused);

            double firstU = Channel(clipped, 0, 0, 0);
            double firstV = Channel(clipped, 0, 0, 1);
            double lastU = Channel(clipped, ClippedExtent - 1, ClippedExtent - 1, 0);
            double lastV = Channel(clipped, ClippedExtent - 1, ClippedExtent - 1, 1);
            double cropDeviation = MaximumCropDeviation(complete, clipped);
            TestContext.WriteLine(
                $"fused={fused} first=({firstU:F6},{firstV:F6}) last=({lastU:F6},{lastV:F6}) "
                + $"cropDeviation={cropDeviation:F6}");

            Assert.Multiple(() =>
            {
                // Nothing is clipped here, so this reading is uncontested and anchors the expected values.
                Assert.That(Channel(complete, 0, 0, 0), Is.EqualTo(0.5 / ContentExtent).Within(0.002));
                Assert.That(
                    Channel(complete, ContentExtent - 1, ContentExtent - 1, 0),
                    Is.EqualTo((ContentExtent - 0.5) / ContentExtent).Within(0.002));

                // The first visible fragment sits one overhang into the complete output, not at its own origin.
                Assert.That(firstU, Is.EqualTo((Overhang + 0.5) / ContentExtent).Within(0.002));
                Assert.That(firstV, Is.EqualTo((Overhang + 0.5) / ContentExtent).Within(0.002));

                // The clip leaves the content's far edge inside the frame, where the normalized coordinate must
                // still reach 1.
                Assert.That(lastU, Is.EqualTo((ContentExtent - 0.5) / ContentExtent).Within(0.002));
                Assert.That(lastV, Is.EqualTo((ContentExtent - 0.5) / ContentExtent).Within(0.002));

                // Rendering part of a whole-source stage must equal rendering all of it and cropping.
                Assert.That(cropDeviation, Is.LessThan(CropInvarianceTolerance));
            });
        });
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    [Category("GpuPassFusionGpu")]
    public void HorizontalFlip_MirrorsTheCompleteOutput_WhenTheRequiredRegionIsAStrictSubset(bool fused)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap flipped = RenderComplete(HorizontalFlipShader, fused);
            using Bitmap clippedFlipped = RenderClipped(HorizontalFlipShader, fused);
            using Bitmap unflipped = RenderComplete(IdentityShader, fused);

            double cropDeviation = MaximumCropDeviation(flipped, clippedFlipped);
            double mirrorEffect = MaximumAlignedDeviation(unflipped, flipped, ContentExtent);
            TestContext.WriteLine(
                $"fused={fused} cropDeviation={cropDeviation:F6} mirrorEffect={mirrorEffect:F6}");

            Assert.Multiple(() =>
            {
                // Non-vacuity: a mirror that moved nothing would satisfy any invariance check.
                Assert.That(mirrorEffect, Is.GreaterThan(0.2), "the mirror must actually move the content");
                Assert.That(cropDeviation, Is.LessThan(CropInvarianceTolerance));
            });
        });
    }

    private static Bitmap RenderComplete(string shader, bool fused)
        => Render(shader, new Rect(0, 0, ContentExtent, ContentExtent), ContentExtent, fused);

    private static Bitmap RenderClipped(string shader, bool fused)
        => Render(
            shader,
            new Rect(-Overhang, -Overhang, ContentExtent, ContentExtent),
            ClippedExtent,
            fused);

    private static Bitmap Render(string shader, Rect content, int frameExtent, bool fused)
    {
        using var node = new WholeSourceProbeNode(content, shader);
        using RenderTarget target = RenderTarget.Create(frameExtent, frameExtent)
            ?? throw new InvalidOperationException("Could not allocate the whole-source origin target.");
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview, 1, 1, new Size(frameExtent, frameExtent)))
        {
            canvas.Clear();
            using var renderer = new RenderNodeRenderer(
                node,
                new RenderNodeRendererOptions
                {
                    DefaultRequest = new RenderNodeRenderRequest
                    {
                        Intent = RenderIntent.Preview,
                        TargetDomain = new Rect(0, 0, frameExtent, frameExtent),
                        OutputScale = 1,
                        MaxWorkingScale = 1,
                        CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                        FusionMode = fused ? FusionMode.Enabled : FusionMode.Disabled,
                    },
                });
            renderer.Render(canvas);
        }

        return target.Snapshot();
    }

    // Both renders place identical content, one shifted by a whole number of texels, so the window offset is an
    // exact integer and no resampling separates the two.
    private static double MaximumCropDeviation(Bitmap complete, Bitmap clipped)
    {
        double worst = 0;
        for (int y = 0; y < ClippedExtent; y++)
        {
            ReadOnlySpan<ushort> completeRow = complete.GetRow<ushort>(y + Overhang);
            ReadOnlySpan<ushort> clippedRow = clipped.GetRow<ushort>(y);
            for (int x = 0; x < ClippedExtent; x++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    double deviation = Math.Abs(
                        Half(completeRow, x + Overhang, channel) - Half(clippedRow, x, channel));
                    if (deviation > worst)
                        worst = deviation;
                }
            }
        }

        return worst;
    }

    private static double MaximumAlignedDeviation(Bitmap first, Bitmap second, int extent)
    {
        double worst = 0;
        for (int y = 0; y < extent; y++)
        {
            ReadOnlySpan<ushort> firstRow = first.GetRow<ushort>(y);
            ReadOnlySpan<ushort> secondRow = second.GetRow<ushort>(y);
            for (int x = 0; x < extent * 4; x++)
            {
                double deviation = Math.Abs(
                    (float)BitConverter.UInt16BitsToHalf(firstRow[x])
                    - (float)BitConverter.UInt16BitsToHalf(secondRow[x]));
                if (deviation > worst)
                    worst = deviation;
            }
        }

        return worst;
    }

    private static double Half(ReadOnlySpan<ushort> row, int x, int channel)
        => (float)BitConverter.UInt16BitsToHalf(row[(x * 4) + channel]);

    private static double Channel(Bitmap bitmap, int x, int y, int channel)
        => Half(bitmap.GetRow<ushort>(y), x, channel);

    private sealed class WholeSourceProbeNode : RenderNode
    {
        private readonly Brush.Resource _fill;
        private readonly RectangleRenderNode _source;
        private readonly ShaderDescription _description;

        public WholeSourceProbeNode(Rect content, string shader)
        {
            var gradient = new LinearGradientBrush();
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(255, 250, 32, 16), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(255, 16, 64, 240), 1));
            _fill = (Brush.Resource)gradient.ToResource(CompositionContext.Default);
            _source = new RectangleRenderNode(content, _fill, null);
            _description = ShaderDescription.WholeSource(
                shader,
                RenderBoundsContract.FullInput,
                static bindings => bindings.Uniform("iResolution", default(Vector2), BindResolution));
        }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.RecordNode(_source, [])[0];
            context.Publish(context.Shader(source, _description));
        }

        protected override void OnDispose(bool disposing)
        {
            _source.Dispose();
            _fill.Dispose();
            base.OnDispose(disposing);
        }

        private static void BindResolution(
            ShaderUniformWriter writer,
            Vector2 value,
            ShaderExecutionContext context)
            => writer.Set(new Vector2(
                context.SemanticOutputSize.Width,
                context.SemanticOutputSize.Height));
    }
}

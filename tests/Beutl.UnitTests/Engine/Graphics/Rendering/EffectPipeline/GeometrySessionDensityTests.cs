using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression cover for the geometry-session density conflation: <see cref="GeometrySession.WorkingScale"/> is the
/// pass's OUTPUT density (the canvas CTM / device-space math), while its input is materialized at its OWN density
/// (<see cref="EffectInput.Density"/>). When a forward-inflated / over-budget input is carried in below the output
/// density (input density 0.5, output density 1.0), a geometry effect that scales its device-space crop/offset by the
/// input density mis-sizes and mis-positions the result. The parity anchor is the same clip fed a matched-density
/// input: the two must land at the same logical position and cover the buffer identically.
/// </summary>
[NonParallelizable]
[TestFixture]
public class GeometrySessionDensityTests
{
    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // A left clip fed an input whose supply density (0.5) is below the pass output density (1.0). The kept region must
    // land at the same logical position and fill the same buffer as a matched-density (1.0) input. Pre-fix, the crop
    // offset and the unscaled input blit both use the input density, so the content halves in size and shifts left.
    [Test]
    public void Clipping_InputDensityBelowOutputDensity_MatchesMatchedDensityRender()
    {
        var inputBounds = new Rect(0, 0, 100, 100);
        // Opaque cyan (red=0) everywhere with a white (red=1) band for x < 64, so the white/cyan boundary marks a
        // known logical column that the clip preserves.
        Action<ImmediateCanvas> draw = canvas =>
        {
            canvas.DrawRectangle(inputBounds, Brushes.Resource.Cyan, null);
            canvas.DrawRectangle(new Rect(0, 0, 64, 100), Brushes.Resource.White, null);
        };

        var clip = new Clipping { Left = { CurrentValue = 40f } };

        using Bitmap matched = RenderClip(clip, draw, inputBounds, EffectiveScale.At(1f));
        using Bitmap lowDensity = RenderClip(clip, draw, inputBounds, EffectiveScale.At(0.5f));

        int matchedEdge = RightmostRedEdge(matched, y: 20);
        int lowEdge = RightmostRedEdge(lowDensity, y: 20);
        TestContext.WriteLine($"white/cyan edge: matched={matchedEdge}, lowDensity={lowEdge}");

        // Far corner opacity is the SIZE signal: a matched-density clip fills the whole buffer; an unscaled 0.5-density
        // blit only reaches the buffer's left/top quadrant, leaving the far corner transparent.
        byte matchedCorner = matched.SKBitmap.GetPixel(matched.Width - 4, matched.Height - 4).Alpha;
        byte lowCorner = lowDensity.SKBitmap.GetPixel(lowDensity.Width - 4, lowDensity.Height - 4).Alpha;
        TestContext.WriteLine($"far-corner alpha: matched={matchedCorner}, lowDensity={lowCorner}");

        Assert.Multiple(() =>
        {
            Assert.That(lowEdge, Is.EqualTo(matchedEdge).Within(3),
                $"the clipped kept region shifted: a 0.5-density input scaled its crop offset by the input density "
                + $"instead of the output density (matched edge {matchedEdge}, observed {lowEdge}).");
            Assert.That(lowCorner, Is.EqualTo(matchedCorner).Within(8),
                $"the clipped kept region under-filled the buffer: the 0.5-density input was blitted unscaled onto the "
                + $"1.0-density output (matched corner alpha {matchedCorner}, observed {lowCorner}).");
        });
    }

    // Drives a single geometry effect over an input op of the given supply density, resolving the output at the full
    // budget (output density 1.0) so the input's lower density is the only density seam, then rasterizes to logical.
    private static Bitmap RenderClip(
        FilterEffect effect, Action<ImmediateCanvas> draw, Rect inputBounds, EffectiveScale inputDensity)
    {
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            inputBounds, draw, hitTest: inputBounds.Contains, onDispose: null, effectiveScale: inputDensity);

        var builder = new EffectGraphBuilder(inputBounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(Composition.CompositionContext.Default));

        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery);
        try
        {
            Rect outBounds = ops[0].Bounds;
            var size = PixelRect.FromRect(outBounds);
            using RenderTarget target = RenderTarget.Create(size.Width, size.Height)!;
            using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: outBounds.Size))
            {
                canvas.Clear();
                using (canvas.PushTransform(Matrix.CreateTranslation(-outBounds.X, -outBounds.Y)))
                {
                    foreach (RenderNodeOperation op in ops)
                        op.Render(canvas);
                }
            }

            return target.Snapshot();
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    // The rightmost x on the given row whose red channel is high (white band) while the pixel is opaque.
    private static int RightmostRedEdge(Bitmap bmp, int y)
    {
        int edge = -1;
        for (int x = 0; x < bmp.Width; x++)
        {
            SKColor px = bmp.SKBitmap.GetPixel(x, y);
            if (px.Alpha > 128 && px.Red > 128)
                edge = x;
        }

        return edge;
    }
}

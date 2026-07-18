using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins the whole-source shader input halo under a downstream deflate (execution-plan §C3.1): a non-invariant
/// whole-source fused pass (<see cref="ColorShift"/>) samples its source at coordinates its backward map claims.
/// When a downstream deflating pass (a fixed <see cref="Clipping"/>) narrows the pass output below that claimed
/// input rect, baking the source into the output-sized target loses the halo the shader reads, so crop-after-shift
/// diverges from shift-then-crop. The fix bakes the source over the claimed rect in a separate buffer and offsets
/// the shader's src sampling, so the kept region matches the un-cropped ColorShift render.
/// </summary>
[NonParallelizable]
[TestFixture]
public class ColorShiftHaloCropTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    // The red channel is shifted +30 px; a fixed clip crops the left 40 px of the shifted output. In the kept window
    // [42,60] the red channel samples logical x in [12,30] — inside the cropped-away halo, so a pre-fix bake reads
    // transparent there and the red goes missing.
    private const int WinX0 = 42, WinX1 = 60, WinY0 = 20, WinY1 = 80;

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    [Test]
    public void ColorShiftThenClip_KeptRegionMatchesUncroppedColorShift()
    {
        ColorShift MakeShift() => new() { RedOffset = { CurrentValue = new PixelPoint(30, 0) } };

        using Bitmap uncropped = RenderChain([MakeShift()]);
        using Bitmap clipped = RenderChain([MakeShift(), MakeLeftClip()]);

        double meanDiff = MeanChannelDiff(uncropped, clipped, WinX0, WinY0, WinX1, WinY1);
        TestContext.WriteLine($"kept-region mean channel diff = {meanDiff:F3} (tolerance 6)");
        Assert.That(meanDiff, Is.LessThanOrEqualTo(6.0),
            "the clipped kept region must match the un-cropped ColorShift (the shader halo must survive the deflate)");
    }

    private static Clipping MakeLeftClip() => new() { Left = { CurrentValue = 40 } };

    // Full-frame opaque input: white for x < 35 (red = 255), cyan for x >= 35 (red = 0). The +30 red shift moves the
    // white/cyan red edge to logical 65, so the kept window is fully white when the halo survives and cyan when it is
    // cropped — a large, red-channel-only divergence.
    private static RenderNodeOperation Input()
        => RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas =>
            {
                canvas.DrawRectangle(s_bounds, Brushes.Resource.Cyan, null);
                canvas.DrawRectangle(new Rect(0, 0, 35, 100), Brushes.Resource.White, null);
            },
            hitTest: s_bounds.Contains);

    private static Bitmap RenderChain(FilterEffect[] effects)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        foreach (FilterEffect effect in effects)
            effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default));

        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [Input()], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery);

        int w = (int)s_bounds.Width, h = (int)s_bounds.Height;
        using RenderTarget target = RenderTarget.Create(w, h)!;
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: s_bounds.Size))
        {
            canvas.Clear(Colors.Black);
            foreach (RenderNodeOperation op in ops)
                op.Render(canvas);
        }

        RenderNodeOperation.DisposeAll(ops);
        return target.Snapshot();
    }

    private static double MeanChannelDiff(Bitmap a, Bitmap b, int x0, int y0, int x1, int y1)
    {
        long sum = 0;
        long count = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                SKColor ca = a.SKBitmap.GetPixel(x, y);
                SKColor cb = b.SKBitmap.GetPixel(x, y);
                sum += Math.Abs(ca.Red - cb.Red);
                sum += Math.Abs(ca.Green - cb.Green);
                sum += Math.Abs(ca.Blue - cb.Blue);
                count += 3;
            }
        }

        return count == 0 ? 0 : (double)sum / count;
    }
}

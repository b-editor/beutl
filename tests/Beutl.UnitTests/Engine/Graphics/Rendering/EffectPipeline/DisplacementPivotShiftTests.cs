using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins the displacement scale/rotation pivot (and its device-space displacement-map child) under a SHIFTED
/// render-time predecessor (feature 004, A3/A5). A dynamic CustomRenderNode that translates its output by
/// <c>(dx,dy)</c> makes the executor set the pass output origin to the shifted op's origin; the pivot and the map are
/// authored purely SIZE-relative (<c>uPivot</c> from <c>Bounds.Width/2 + CenterX</c>, the map over
/// <c>new Rect(Bounds.Size)</c>), so both anchor to the pass's coord origin — which the executor already sets to the
/// shifted content origin — and the warp travels rigidly with the content for free. The invariant is translation
/// equivalence: the shifted render's kept window at <c>(x+dx, y+dy)</c> equals the unshifted render at <c>(x,y)</c>.
/// This is a regression guard for the CORRECT existing behavior: introducing a describe-time-origin dependency in the
/// pivot/map (e.g. subtracting the execution output origin) would world-anchor them and break the translation, which
/// this test catches (the scale warp diverges by ~16 mean channel units).
/// </summary>
[NonParallelizable]
[TestFixture]
public class DisplacementPivotShiftTests
{
    private static readonly Rect s_bounds = new(0, 0, 160, 120);
    private const int ShiftX = 30, ShiftY = 24;

    // A window well inside both the unshifted kept region and (once offset by the shift) the shifted one.
    private const int WinX0 = 40, WinY0 = 30, WinX1 = 120, WinY1 = 90;

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory ??= Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
        VulkanTestEnvironment.EnsureAvailable();
    }

    [Test]
    public void DisplacementMapScale_ShiftedPredecessor_TranslatesRigidly()
    {
        AssertShiftTranslatesRigidly(MakeScaleEffect);
    }

    [Test]
    public void DisplacementMapRotation_ShiftedPredecessor_TranslatesRigidly()
    {
        AssertShiftTranslatesRigidly(MakeRotationEffect);
    }

    private static void AssertShiftTranslatesRigidly(Func<DisplacementMapEffect> makeEffect)
    {
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap plain = RenderChain([], ShapeInput);
            using Bitmap unshifted = RenderChain([makeEffect()], ShapeInput);
            using Bitmap shifted = RenderChain(
                [new ShiftingCustomNodeEffect(new Point(ShiftX, ShiftY)), makeEffect()], ShapeInput);

            // The warp must be spatially non-trivial in the window, otherwise the translation check below is vacuous.
            double warp = MeanChannelDiffShifted(plain, unshifted, WinX0, WinY0, WinX1, WinY1, 0, 0);
            double meanDiff = MeanChannelDiffShifted(unshifted, shifted, WinX0, WinY0, WinX1, WinY1, ShiftX, ShiftY);
            TestContext.WriteLine($"warp magnitude = {warp:F3}; translation diff (shift-corrected) = {meanDiff:F3}");
            Assert.That(warp, Is.GreaterThan(5.0),
                "sanity: the displacement effect must genuinely warp the window (else the translation check is vacuous)");
            Assert.That(meanDiff, Is.LessThanOrEqualTo(8.0),
                "a shifted predecessor must translate the displacement warp rigidly (pivot/map track the content)");
        });
    }

    private static DisplacementMapEffect MakeScaleEffect()
    {
        var effect = new DisplacementMapEffect
        {
            DisplacementMap = { CurrentValue = HorizontalRamp() },
            Signed = { CurrentValue = true },
            Channel = { CurrentValue = DisplacementMapChannel.Red },
        };
        effect.Transform.CurrentValue = new DisplacementMapScaleTransform
        {
            Scale = { CurrentValue = 100 },
            ScaleX = { CurrentValue = 160 },
            ScaleY = { CurrentValue = 160 },
        };
        return effect;
    }

    private static DisplacementMapEffect MakeRotationEffect()
    {
        var effect = new DisplacementMapEffect
        {
            DisplacementMap = { CurrentValue = HorizontalRamp() },
            Signed = { CurrentValue = true },
            Channel = { CurrentValue = DisplacementMapChannel.Red },
        };
        effect.Transform.CurrentValue = new DisplacementMapRotationTransform
        {
            Rotation = { CurrentValue = 70 },
        };
        return effect;
    }

    private static LinearGradientBrush HorizontalRamp()
    {
        var map = new LinearGradientBrush();
        map.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        map.EndPoint.CurrentValue = new RelativePoint(1, 0, RelativeUnit.Relative);
        map.GradientStops.Add(new GradientStop(Colors.Black, 0));
        map.GradientStops.Add(new GradientStop(Colors.White, 1));
        return map;
    }

    // A sharp off-centre feature set so the spatially varying warp produces high-contrast spatial detail to compare.
    private static RenderNodeOperation ShapeInput()
    {
        return RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas =>
            {
                canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null);
                canvas.DrawRectangle(new Rect(30, 24, 50, 40), Brushes.Resource.Red, null);
                canvas.DrawRectangle(new Rect(90, 60, 40, 44), Brushes.Resource.Blue, null);
            },
            hitTest: s_bounds.Contains);
    }

    private static Bitmap RenderChain(FilterEffect[] effects, Func<RenderNodeOperation> input)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        foreach (FilterEffect effect in effects)
            effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default));

        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [input()], outputScale: 1f, workingScale: 1f,
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

    // Compares the unshifted render at (x,y) against the shifted render at (x+dx, y+dy): a rigidly translated warp
    // makes these equal.
    private static double MeanChannelDiffShifted(
        Bitmap unshifted, Bitmap shifted, int x0, int y0, int x1, int y1, int dx, int dy)
    {
        long sum = 0;
        long count = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                SkiaSharp.SKColor a = unshifted.SKBitmap.GetPixel(x, y);
                SkiaSharp.SKColor b = shifted.SKBitmap.GetPixel(x + dx, y + dy);
                sum += Math.Abs(a.Red - b.Red);
                sum += Math.Abs(a.Green - b.Green);
                sum += Math.Abs(a.Blue - b.Blue);
                count += 3;
            }
        }

        return count == 0 ? 0 : (double)sum / count;
    }
}

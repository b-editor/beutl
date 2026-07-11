using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression for the displacement-map child shader under a fan-out (feature 004, A4). A SplitEffect fans the current
/// op set into one branch per tile, and a following DisplacementMapEffect in the same group runs its whole-source
/// shader once PER branch. The deferred map child is a <see cref="ChildBinding.Deferred"/>: its factory MUST return a
/// fresh shader every call because the executor disposes each per-pass product after that branch's draw. A holder that
/// cached the resolved shader across passes handed branches 2+ the shader branch 1 already disposed — a native
/// use-after-dispose plus a double-dispose (and a stale uMapPresent). This pins that every fan-out branch rebuilds its
/// own shader: the group renders without throwing and all four tiles show the displacement warp.
/// </summary>
[NonParallelizable]
[TestFixture]
public class DisplacementSplitFanOutTests
{
    private static readonly Rect s_bounds = new(0, 0, 160, 120);

    // Interior windows well inside each of the four 2x2 tiles (avoiding the x=80 / y=60 seams).
    private static (int X0, int Y0, int X1, int Y1, string Name)[] TileWindows() =>
    [
        (10, 8, 70, 52, "top-left"),
        (90, 8, 150, 52, "top-right"),
        (10, 68, 70, 112, "bottom-left"),
        (90, 68, 150, 112, "bottom-right"),
    ];

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory ??= Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
        VulkanTestEnvironment.EnsureAvailable();
    }

    [Test]
    public void SplitFanOut_ThenDisplacement_RebuildsChildShaderPerBranch()
    {
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Bitmap? splitOnly = null;
            Bitmap? displaced = null;

            // The regression: branches 2+ reused branch 1's disposed per-pass shader (use-after-dispose + double
            // dispose), so the second render deterministically threw on the disposed native handle.
            Assert.DoesNotThrow(
                () =>
                {
                    splitOnly = RenderGroup(MakeSplitOnlyGroup());
                    displaced = RenderGroup(MakeSplitThenDisplacementGroup());
                },
                "a split fan-out followed by a displacement map must rebuild the map child per branch, not reuse a disposed shader");

            using (splitOnly)
            using (displaced)
            {
                // Each of the four 2x2 tiles carries identical content and samples the same map region anchored at its
                // own origin, so every fan-out branch that resolves its OWN fresh map shader must warp identically. The
                // bug left branch 0 correct but branches 1..3 reusing branch 0's disposed shader, so their tiles
                // diverged. Assert the first branch genuinely warps, then that every later branch matches it.
                (int X0, int Y0, int X1, int Y1, string Name)[] windows = TileWindows();
                double branch0 = MeanChannelDiff(splitOnly!, displaced!, windows[0].X0, windows[0].Y0, windows[0].X1, windows[0].Y1);
                TestContext.WriteLine($"branch 0 ({windows[0].Name}): warp diff = {branch0:F3}");
                Assert.That(branch0, Is.GreaterThan(5.0),
                    "sanity: the first branch must genuinely warp (else the cross-branch equality check is vacuous)");

                for (int i = 1; i < windows.Length; i++)
                {
                    (int x0, int y0, int x1, int y1, string name) = windows[i];
                    double diff = MeanChannelDiff(splitOnly!, displaced!, x0, y0, x1, y1);
                    TestContext.WriteLine($"branch {i} ({name}): warp diff = {diff:F3}");
                    Assert.That(diff, Is.EqualTo(branch0).Within(1.0),
                        $"the {name} tile must warp identically to the first branch — every fan-out branch rebuilds its own map shader");
                }
            }
        });
    }

    private static FilterEffectGroup MakeSplitOnlyGroup()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(MakeSplit());
        return group;
    }

    private static FilterEffectGroup MakeSplitThenDisplacementGroup()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(MakeSplit());
        group.Children.Add(MakeSignedTranslateEffect());
        return group;
    }

    private static SplitEffect MakeSplit()
    {
        return new SplitEffect
        {
            HorizontalDivisions = { CurrentValue = 2 },
            VerticalDivisions = { CurrentValue = 2 },
            HorizontalSpacing = { CurrentValue = 0f },
            VerticalSpacing = { CurrentValue = 0f },
        };
    }

    private static DisplacementMapEffect MakeSignedTranslateEffect()
    {
        var effect = new DisplacementMapEffect
        {
            DisplacementMap = { CurrentValue = HorizontalRamp() },
            Signed = { CurrentValue = true },
            Channel = { CurrentValue = DisplacementMapChannel.Red },
        };
        effect.Transform.CurrentValue = new DisplacementMapTranslateTransform
        {
            X = { CurrentValue = 40 },
            Y = { CurrentValue = 30 },
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

    // The same sharp feature at the same within-tile position in every quadrant, so the four 2x2 tiles are identical
    // content and a correct per-branch warp shifts the same edge by the same amount in each — making cross-branch
    // equality an exact invariant.
    private static RenderNodeOperation ShapeInput()
    {
        return RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas =>
            {
                canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null);
                canvas.DrawRectangle(new Rect(15, 10, 45, 35), Brushes.Resource.Red, null);
                canvas.DrawRectangle(new Rect(95, 10, 45, 35), Brushes.Resource.Red, null);
                canvas.DrawRectangle(new Rect(15, 70, 45, 35), Brushes.Resource.Red, null);
                canvas.DrawRectangle(new Rect(95, 70, 45, 35), Brushes.Resource.Red, null);
            },
            hitTest: s_bounds.Contains);
    }

    private static Bitmap RenderGroup(FilterEffectGroup group)
    {
        using FilterEffect.Resource resource = (FilterEffect.Resource)group.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        group.Describe(builder, resource);

        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [ShapeInput()], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool);

        int w = (int)s_bounds.Width, h = (int)s_bounds.Height;
        using RenderTarget target = RenderTarget.Create(w, h)!;
        using (var canvas = new ImmediateCanvas(target, 1f, logicalSize: s_bounds.Size))
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
                SkiaSharp.SKColor ca = a.SKBitmap.GetPixel(x, y);
                SkiaSharp.SKColor cb = b.SKBitmap.GetPixel(x, y);
                sum += Math.Abs(ca.Red - cb.Red);
                sum += Math.Abs(ca.Green - cb.Green);
                sum += Math.Abs(ca.Blue - cb.Blue);
                count += 3;
            }
        }

        return count == 0 ? 0 : (double)sum / count;
    }
}

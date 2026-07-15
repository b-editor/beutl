using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the AutoClip output-bounds tightening (feature 004): an AutoClip
/// <see cref="Clipping"/> over a drawable with transparent margins must emit an operation whose
/// <see cref="RenderNodeOperation.Bounds"/> equal the detected content sub-rect — matching the legacy imperative
/// <c>Clipping.Apply</c>, which sized its output target to the detected clip rect. Before the fix the render-time
/// geometry pass returned a full-size, transparent-margined operation, so downstream bounds/hit-testing diverged
/// from legacy (the composited op's rect, a bounds-dependent downstream effect, and hit-testing all saw the full
/// input rect). The kept pixels are byte-identical either way; only the bounds changed.
/// </summary>
[NonParallelizable]
[TestFixture]
public class AutoClipOutputBoundsTightenTests
{
    private static readonly Rect s_input = new(0, 0, 100, 100);
    private static readonly Rect s_content = new(30, 30, 40, 40);

    // The legacy Apply's tightened rect for content at [30,70) x [30,70) on a 100x100 input at density 1: the detected
    // margins are (30,30,31,31) device px, so the clip rect is (30,30,39,39), and the leading-edge ceiling/-1 offset
    // (thickness.Left/Top > 0) shifts the origin back by one px, giving NewBounds = (29,29,39,39).
    private static readonly Rect s_tightened = new(29, 29, 39, 39);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // The core regression: the emitted operation's bounds tighten to the content rect, not the full input rect.
    [Test]
    public void AutoClip_ContentWithTransparentMargins_TightensOutputBoundsToContentRect()
    {
        RenderNodeOperation[] ops = RenderAutoClip();
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1), "a non-empty auto-clip produces exactly one output");

            Rect bounds = ops[0].Bounds;
            Assert.Multiple(() =>
            {
                Assert.That(bounds, Is.EqualTo(s_tightened),
                    "AutoClip must tighten its output to the detected content sub-rect (legacy Apply semantics), "
                    + $"not the full input rect {s_input}");
                Assert.That(bounds, Is.Not.EqualTo(s_input),
                    "the pre-fix full-size, transparent-margined output rect is the regression being restored");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    // The kept pixels are unchanged: rasterizing the tightened op and comparing to the same window cropped out of a
    // full un-clipped render must match byte-for-byte (the tightening removes only transparent margins).
    [Test]
    public void AutoClip_KeptRegionPixels_MatchUnclippedCrop()
    {
        RenderNodeOperation[] clipped = RenderAutoClip();
        using RenderNodeOperation unclipped = MakeContentRect(s_input, s_content);
        try
        {
            Assert.That(clipped, Has.Length.EqualTo(1));
            Rect bounds = clipped[0].Bounds;

            using Bitmap clippedBmp = Rasterize(clipped[0], bounds);
            using Bitmap unclippedBmp = Rasterize(unclipped, bounds);

            long maxDiff = 0;
            for (int y = 0; y < clippedBmp.Height; y++)
            {
                for (int x = 0; x < clippedBmp.Width; x++)
                {
                    SKColor a = clippedBmp.SKBitmap.GetPixel(x, y);
                    SKColor b = unclippedBmp.SKBitmap.GetPixel(x, y);
                    maxDiff = Math.Max(maxDiff, Math.Abs(a.Red - b.Red));
                    maxDiff = Math.Max(maxDiff, Math.Abs(a.Green - b.Green));
                    maxDiff = Math.Max(maxDiff, Math.Abs(a.Blue - b.Blue));
                    maxDiff = Math.Max(maxDiff, Math.Abs(a.Alpha - b.Alpha));
                }
            }

            Assert.That(maxDiff, Is.Zero,
                "the kept region must be byte-identical to the same window of the un-clipped render");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(clipped);
        }
    }

    // Hit-testing coherence: a point inside the tightened bounds hits; a point in the (now-cropped-away) transparent
    // margin does not. Pre-fix the full-size op hit-tested its whole bounds, so a margin point stayed selectable.
    [Test]
    public void AutoClip_HitTest_HonorsTightenedBounds()
    {
        RenderNodeOperation[] ops = RenderAutoClip();
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1));
            RenderNodeOperation op = ops[0];

            Assert.Multiple(() =>
            {
                Assert.That(op.HitTest(new Point(45, 45)), Is.True, "a point inside the content must hit");
                Assert.That(op.HitTest(new Point(5, 5)), Is.False,
                    "a point in the transparent margin cropped away by AutoClip must not hit");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    // A downstream bounds-inflating pass (DropShadow) must see the AutoClip's TIGHTENED output, not its un-shrunk
    // full-input rect. Pre-fix the linear non-invariant branch sized DropShadow from the frame-start resolution.OutputRoi
    // (the un-shrunk full input), re-inflating the full-size rect. DropShadow keeps the original at the tightened
    // origin (29,29) plus the zero-sigma shadow at +(20,20): union(tightened, tightened+(20,20)) = (29,29,59,59).
    [Test]
    public void AutoClipThenDropShadow_PublishedBoundsDeriveFromTightenedInput()
    {
        var expectedTightenedDerived = new Rect(29, 29, 59, 59);
        var fullInputDerived = new Rect(0, 0, 120, 120);

        RenderNodeOperation[] ops = RenderAutoClipThenDropShadow();
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1), "a non-empty auto-clip then drop-shadow produces one output");

            Rect bounds = ops[0].Bounds;
            Assert.Multiple(() =>
            {
                Assert.That(bounds, Is.EqualTo(expectedTightenedDerived),
                    "the drop-shadow output must be the forward map of the TIGHTENED auto-clip content, "
                    + $"not the full-input-derived {fullInputDerived}");
                Assert.That(bounds, Is.Not.EqualTo(fullInputDerived),
                    "the pre-fix full-input-derived bounds (tightening lost) are the regression being restored");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    [Test]
    public void AutoClipThenCompute_UsesTightenedInputBoundsForDestination()
    {
        var clip = new Clipping();
        clip.AutoClip.CurrentValue = true;

        FilterEffect.Resource resource = clip.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(s_input, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        clip.Describe(builder, resource);

        Rect observedBounds = Rect.Invalid;
        builder.Compute(ComputeNodeDescriptor.Create(
            dispatch: static _ => { },
            passCount: 1,
            bounds: BoundsContract.FullFrame,
            fallback: ComputeFallbackPolicy.Cpu(session => observedBounds = session.Bounds),
            structuralToken: "auto-clip-compute-bounds"));

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources resources = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);

        IDisposable computeFallbackHook = PlanExecutor.UseTestHooks(static hooks => hooks.ForceComputeFallback = true);
        RenderNodeOperation[] outputs;
        try
        {
            outputs = PlanExecutor.Execute(
                plan, resources, [MakeContentRect(s_input, s_content)], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
        }
        finally
        {
            computeFallbackHook.Dispose();
        }

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(observedBounds, Is.EqualTo(s_tightened),
                    "the compute destination must use the AutoClip operation's runtime-tightened bounds");
                Assert.That(outputs, Has.Length.EqualTo(1));
                Assert.That(outputs[0].Bounds, Is.EqualTo(s_tightened));
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    private static RenderNodeOperation[] RenderAutoClip()
    {
        var clip = new Clipping();
        clip.AutoClip.CurrentValue = true;

        FilterEffect.Resource resource = clip.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeContentRect(s_input, s_content)], RenderIntent.Delivery);
        return node.Process(context);
    }

    private static RenderNodeOperation[] RenderAutoClipThenDropShadow()
    {
        var clip = new Clipping();
        clip.AutoClip.CurrentValue = true;

        var shadow = new DropShadow();
        shadow.Position.CurrentValue = new Point(20, 20);
        shadow.Sigma.CurrentValue = new Size(0, 0);
        shadow.Color.CurrentValue = Colors.Red;
        shadow.ShadowOnly.CurrentValue = false;

        var group = new FilterEffectGroup();
        group.Children.Add(clip);
        group.Children.Add(shadow);

        FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeContentRect(s_input, s_content)], RenderIntent.Delivery);
        return node.Process(context);
    }

    private static RenderNodeOperation MakeContentRect(Rect bounds, Rect content)
        => RenderNodeOperation.CreateLambda(
            bounds,
            canvas => canvas.DrawRectangle(content, Brushes.Resource.White, null),
            hitTest: content.Contains);

    private static Bitmap Rasterize(RenderNodeOperation op, Rect window)
    {
        var size = PixelRect.FromRect(window);
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null (raster surface unavailable).");
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: window.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-window.X, -window.Y)))
            {
                op.Render(canvas);
            }
        }

        return target.Snapshot();
    }
}

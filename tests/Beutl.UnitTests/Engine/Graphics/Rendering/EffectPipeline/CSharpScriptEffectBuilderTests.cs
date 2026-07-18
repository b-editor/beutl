using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Restores the C# script effect's declarative-authoring capability (feature 004, contracts/breaking-changes.md).
/// A <see cref="CSharpScriptEffect"/> script now runs inside <c>Describe</c> and appends node descriptors through
/// an <c>EffectGraphBuilder</c> (<c>Builder</c>) exactly like a compiled effect author, so it can apply image
/// filters (<c>Builder.Blur</c>, <c>Builder.DropShadow</c>, …), color filters (<c>Builder.Saturate</c>, …) and
/// custom canvas drawing (<c>Builder.Geometry</c>), fuse with adjacent invariant effects, and change structure by
/// branching on the animated parameters. The render-comparison cases are Vulkan-gated; the structural-key and
/// exception cases are pure CPU checks.
/// </summary>
[NonParallelizable]
[TestFixture]
public class CSharpScriptEffectBuilderTests
{
    private static readonly PixelSize s_size = new(160, 120);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // Test 1 — capability restoration: a script `Builder.Blur(...)` renders equivalently to the built-in Blur with
    // the same sigma (built-in Blur.Describe is itself `builder.Blur(r.Sigma)`, so the script must reach parity).
    // Red before the redesign: the script had no Builder global, so it could not blur at all.
    [Test]
    public void ScriptBlur_MatchesBuiltinBlur()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap scripted = RenderShape(() =>
            {
                var e = new CSharpScriptEffect();
                e.Script.CurrentValue = "Builder.Blur(new Size(4, 4));";
                return e;
            });
            using Bitmap builtin = RenderShape(() =>
            {
                var e = new Blur();
                e.Sigma.CurrentValue = new Size(4, 4);
                return e;
            });

            AssertMatches(builtin, scripted, "script Builder.Blur vs built-in Blur");
        });
    }

    // Test 2 — composition: a script emitting [Saturate color filter + Geometry drawing + DropShadow] produces the
    // same executable graph as the identical chain authored directly against a builder. Both graphs run through the
    // same low-level execution path so the comparison isolates the script's describe wiring.
    [Test]
    public void ScriptComposition_MatchesManuallyAuthoredChain()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(0, 0, s_size.Width, s_size.Height);

            const string script =
                "Builder.Saturate(1.4f);\n"
                + "Builder.Geometry(session =>\n"
                + "{\n"
                + "    var canvas = session.OpenCanvas();\n"
                + "    using (canvas.PushDeviceSpace())\n"
                + "        session.Inputs[0].Draw(canvas, default);\n"
                + "    canvas.DrawEllipse(new Rect(54, 34, 32, 32), Brushes.Resource.Cyan, null);\n"
                + "});\n"
                + "Builder.DropShadow(new Point(6, 6), new Size(3, 3), Colors.Black);";

            var effect = new CSharpScriptEffect();
            effect.Script.CurrentValue = script;
            var resource = (FilterEffect.Resource)effect.ToResource(CompositionContext.Default);

            var scriptBuilder = new EffectGraphBuilder(bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
            effect.Describe(scriptBuilder, resource);
            using EffectGraph scriptGraph = scriptBuilder.Build();

            var refBuilder = new EffectGraphBuilder(bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
            refBuilder.Saturate(1.4f);
            refBuilder.Geometry(session =>
            {
                var canvas = session.OpenCanvas();
                using (canvas.PushDeviceSpace())
                    session.Inputs[0].Draw(canvas, default);
                canvas.DrawEllipse(new Rect(54, 34, 32, 32), Brushes.Resource.Cyan, null);
            });
            refBuilder.DropShadow(new Point(6, 6), new Size(3, 3), Colors.Black);
            using EffectGraph refGraph = refBuilder.Build();

            using Bitmap scripted = RenderGraph(scriptGraph, bounds);
            using Bitmap reference = RenderGraph(refGraph, bounds);

            AssertMatches(reference, scripted, "script composition vs manual chain");
        });
    }

    // Test 3 — fusion: a script-emitted color filter between two built-in invariant effects (Invert) is itself a
    // coordinate-invariant color node, so the whole run fuses into a single GPU pass. Red before the redesign: the
    // script emitted a never-fused Geometry node that split the invariant run into three passes.
    [Test]
    public void ScriptColorFilter_FusesBetweenInvariantEffects()
    {
        VulkanTestEnvironment.EnsureAvailable();
        PipelineDiagnosticsSnapshot counters = VulkanTestEnvironment.InvokeOnRenderThread(() =>
            RenderAndSnapshot(RenderShapeResource(() =>
            {
                var group = new FilterEffectGroup();
                group.Children.Add(new Invert { Amount = { CurrentValue = 1f } });
                var scriptSaturate = new CSharpScriptEffect();
                scriptSaturate.Script.CurrentValue = "Builder.Saturate(1.4f);";
                group.Children.Add(scriptSaturate);
                group.Children.Add(new Invert { Amount = { CurrentValue = 1f } });
                return group;
            })));

        Assert.Multiple(() =>
        {
            Assert.That(counters.GpuPasses, Is.EqualTo(1),
                "a script color filter between two invariant effects fuses into one pass");
            Assert.That(counters.TargetAllocations, Is.LessThanOrEqualTo(1), "at most one intermediate");
            Assert.That(counters.FlushSyncs, Is.EqualTo(0), "Skia-only plan has no backend-transition sync");
        });
    }

    // Test 4a — structure change (structural key): a script branching on Progress emits different structures on
    // either side of the threshold. Same side ⇒ equal keys (one plan); crossing ⇒ different keys (recompile). Red
    // before the redesign: the script always emitted one fixed-token Geometry node, so its key never varied.
    [Test]
    public void ScriptBranchingOnProgress_ChangesStructuralKeyAcrossThreshold()
    {
        var effect = new CSharpScriptEffect();
        effect.TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        effect.Script.CurrentValue = "if (Progress > 0.5f) Builder.Blur(new Size(4, 4));";

        StructuralKey below1 = KeyAtProgress(effect, 0.2);
        StructuralKey below2 = KeyAtProgress(effect, 0.4);
        StructuralKey above1 = KeyAtProgress(effect, 0.6);
        StructuralKey above2 = KeyAtProgress(effect, 0.8);

        Assert.Multiple(() =>
        {
            Assert.That(below1, Is.EqualTo(below2), "same side of the threshold shares a structure (one plan)");
            Assert.That(above1, Is.EqualTo(above2), "same side of the threshold shares a structure");
            Assert.That(above1, Is.Not.EqualTo(below1), "crossing the threshold changes the emitted structure");
        });
    }

    // Test 4b — structure change (recompile count): the same Progress-branching script driven across frames through
    // one persistent render node compiles the plain structure once, recompiles exactly once as it crosses the
    // threshold into the blurred structure, hits the cache on the stable frames of each side, and the blurred frames
    // carry wider output bounds than the plain frames. (The plan cache is single-entry, so a re-cross would also
    // recompile; the monotonic crossing isolates the "exactly one recompile at the threshold" property.)
    [Test]
    public void ScriptBranchingOnProgress_RecompilesExactlyOnceAtThreshold()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ProgramCache.Clear();
            var effect = new CSharpScriptEffect();
            effect.TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
            effect.Script.CurrentValue = "if (Progress > 0.5f) Builder.Blur(new Size(4, 4));";

            // Progress: 0.2, 0.4 (plain) -> 0.6, 0.8 (blur) — crosses the threshold once.
            (PipelineDiagnosticsSnapshot snap, Rect bounds)[] frames =
                DriveProgress(effect, [0.2, 0.4, 0.6, 0.8]);

            Assert.Multiple(() =>
            {
                Assert.That(frames[0].snap.PlanCompilations, Is.EqualTo(1), "frame 0 compiles the plain structure");
                Assert.That(frames[2].snap.PlanCompilations, Is.EqualTo(1),
                    "crossing the threshold recompiles once");
                Assert.That(frames[1].snap.PlanCompilations + frames[3].snap.PlanCompilations, Is.EqualTo(0),
                    "the stable frames of each side hit the cache");
                Assert.That(frames[2].bounds.Width, Is.GreaterThan(frames[0].bounds.Width),
                    "the blurred structure inflates the output bounds");
            });
        });
    }

    // Test 6 — runtime-exception semantics: a script that throws while describing must not crash and must leave the
    // shared builder clean (its partial appends discarded), so the effect degrades to identity. Red before the
    // redesign: the script ran at execute time inside a Geometry callback, so Describe always appended one node.
    [Test]
    public void ScriptThrowingWhileDescribing_RollsBackToIdentity()
    {
        var effect = new CSharpScriptEffect();
        effect.Script.CurrentValue =
            "Builder.Saturate(1.4f);\n"
            + "throw new System.InvalidOperationException(\"boom\");";
        var resource = (FilterEffect.Resource)effect.ToResource(CompositionContext.Default);

        var builder = new EffectGraphBuilder(
            new Rect(0, 0, s_size.Width, s_size.Height), outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);

        Assert.That(() => effect.Describe(builder, resource), Throws.Nothing,
            "a script runtime exception must not escape Describe");

        using EffectGraph graph = builder.Build();
        Assert.That(graph.Nodes, Is.Empty,
            "the partial Saturate append is rolled back, leaving the identity graph");
    }

    // A script that throws must not poison effects authored before it into the same shared builder (a chain shares
    // one builder across all its children, so rollback must be scoped to the script's own appends).
    [Test]
    public void ScriptThrowing_PreservesEarlierEffectsInSharedBuilder()
    {
        var throwing = new CSharpScriptEffect();
        throwing.Script.CurrentValue = "Builder.Blur(new Size(4, 4));\nthrow new System.Exception(\"x\");";
        var throwingResource = (FilterEffect.Resource)throwing.ToResource(CompositionContext.Default);

        var builder = new EffectGraphBuilder(
            new Rect(0, 0, s_size.Width, s_size.Height), outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Saturate(1.2f);

        throwing.Describe(builder, throwingResource);

        using EffectGraph graph = builder.Build();
        Assert.That(graph.Nodes, Has.Count.EqualTo(1),
            "the earlier Saturate survives; only the throwing script's appends are rolled back");
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    private static StructuralKey KeyAtProgress(CSharpScriptEffect effect, double progressSeconds)
    {
        var ctx = new CompositionContext(TimeSpan.FromSeconds(progressSeconds));
        var resource = (FilterEffect.Resource)effect.ToResource(ctx);
        var builder = new EffectGraphBuilder(new Rect(0, 0, 100, 100), outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        effect.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        return StructuralKey.Compute(graph);
    }

    private static (PipelineDiagnosticsSnapshot snap, Rect bounds)[] DriveProgress(
        CSharpScriptEffect effect, double[] progresses)
    {
        var resource = (FilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();
        var result = new (PipelineDiagnosticsSnapshot, Rect)[progresses.Length];

        for (int f = 0; f < progresses.Length; f++)
        {
            pool.Trim(f);
            bool updateOnly = false;
            resource.Update(effect, new CompositionContext(TimeSpan.FromSeconds(progresses[f])), ref updateOnly);
            node.Update(resource);

            diagnostics.Reset();
            var context = new RenderNodeContext([Input()], RenderIntent.Delivery) { Diagnostics = diagnostics, Pool = pool };
            RenderNodeOperation[] ops = node.Process(context);
            Rect bounds = ops.Aggregate<RenderNodeOperation, Rect>(default, (u, op) => u.Union(op.Bounds));
            RenderNodeOperation.DisposeAll(ops);
            result[f] = (diagnostics.Snapshot(), bounds);
        }

        return result;
    }

    private static RenderNodeOperation Input()
    {
        var bounds = new Rect(0, 0, s_size.Width, s_size.Height);
        return RenderNodeOperation.CreateLambda(
            bounds,
            canvas => canvas.DrawRectangle(bounds.Deflate(24), Brushes.Resource.White, null),
            hitTest: bounds.Contains);
    }

    private static Bitmap RenderGraph(EffectGraph graph, Rect bounds)
    {
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [ColorInput(bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
        return Rasterize(ops, bounds);
    }

    private static RenderNodeOperation ColorInput(Rect bounds)
    {
        return RenderNodeOperation.CreateLambda(
            bounds,
            canvas =>
            {
                canvas.DrawRectangle(bounds.Deflate(16), Brushes.Resource.Red, null);
                canvas.DrawRectangle(new Rect(bounds.X + 16, bounds.Y + 16, 60, 40), Brushes.Resource.Blue, null);
            },
            hitTest: bounds.Contains);
    }

    private static Bitmap Rasterize(RenderNodeOperation[] ops, Rect bounds)
    {
        var size = PixelRect.FromRect(bounds);
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: bounds.Size))
        {
            canvas.Clear(Colors.Black);
            using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
            {
                foreach (RenderNodeOperation op in ops)
                    op.Render(canvas);
            }
        }

        RenderNodeOperation.DisposeAll(ops);
        return target.Snapshot();
    }

    private static Bitmap RenderShape(Func<FilterEffect> makeEffect)
        => GoldenImageHarness.RenderAtScale(RenderShapeResource(makeEffect), s_size, 1f);

    private static Drawable.Resource RenderShapeResource(Func<FilterEffect> makeEffect)
    {
        var fill = new LinearGradientBrush();
        fill.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        fill.EndPoint.CurrentValue = new RelativePoint(1, 1, RelativeUnit.Relative);
        fill.GradientStops.Add(new GradientStop(Colors.Red, 0));
        fill.GradientStops.Add(new GradientStop(Colors.Lime, 0.5f));
        fill.GradientStops.Add(new GradientStop(Colors.Blue, 1));

        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 120;
        shape.Height.CurrentValue = 80;
        shape.Fill.CurrentValue = fill;

        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 12f;
        shape.Transform.CurrentValue = rotation;

        shape.FilterEffect.CurrentValue = makeEffect();
        return shape.ToResource(CompositionContext.Default);
    }

    private static PipelineDiagnosticsSnapshot RenderAndSnapshot(Drawable.Resource resource)
    {
        using RenderTarget target = RenderTarget.Create(s_size.Width, s_size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: s_size.ToSize(1));
        canvas.Clear(Colors.Black);

        using var node = new DrawableRenderNode(resource);
        using (var ctx = new GraphicsContext2D(node, s_size.ToSize(1), 1f))
        {
            resource.GetOriginal().Render(ctx, resource);
        }

        var processor = new RenderNodeProcessor(node, useRenderCache: false, RenderIntent.Delivery, outputScale: 1f);
        RenderNodeOperation[] ops = processor.PullToRoot();
        foreach (RenderNodeOperation op in ops)
        {
            op.Render(canvas);
            op.Dispose();
        }

        return processor.Diagnostics.Snapshot();
    }

    private static void AssertMatches(Bitmap expected, Bitmap actual, string because)
    {
        double ssim = ImageMetrics.Ssim(expected, actual);
        double mae = ImageMetrics.MeanAbsoluteError(expected, actual);
        TestContext.WriteLine($"{because}: SSIM={ssim:F4} MAE={mae:F4}");
        Assert.Multiple(() =>
        {
            Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin), $"SSIM ({because})");
            Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), $"MAE ({because})");
        });
    }
}

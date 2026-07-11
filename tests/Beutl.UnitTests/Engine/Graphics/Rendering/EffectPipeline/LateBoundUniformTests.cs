using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression cover for findings F1 and F2 (feature 004): a density-/size-dependent shader value frozen at the
/// describe-time <see cref="EffectGraphBuilder.WorkingScale"/> is wrong when the resource-resolution re-clamp
/// (<c>ClampWorkingScaleToBufferBudget</c>) executes the pass BELOW that density. F1 covers late-bound
/// <em>uniforms</em> (via <c>DensityScaledFloat2</c> / <c>Deferred</c>); F2 covers a late-bound child
/// <em>shader</em> — a cross-sampled displacement map whose device-space lookup mis-scales when baked at the
/// describe density — via <see cref="ChildBinding.Deferred"/>, whose product the executor owns per pass.
/// </summary>
[NonParallelizable]
[TestFixture]
public class LateBoundUniformTests
{
    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // A forward-inflated ColorShift whose expanded buffer crosses the (overridden) budget executes at w=0.5, below
    // the describe-time w=1.0. The red-channel offset must land at the CORRECT logical position (60 px), scaled by
    // the EXECUTION density. Pre-fix the offset is baked at the describe density and over-shifts to ~80 px.
    [Test]
    public void ColorShift_ForwardInflatedBelowDescribeDensity_ShiftsRedToCorrectLogicalPosition()
    {
        var inputBounds = new Rect(0, 0, 100, 100);
        // Red edge in the source: white (red=1) for x < 40, cyan (red=0) for x >= 40.
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            inputBounds,
            canvas =>
            {
                canvas.DrawRectangle(inputBounds, Brushes.Resource.White, null);
                canvas.DrawRectangle(new Rect(40, 0, 60, 100), Brushes.Resource.Cyan, null);
            },
            hitTest: inputBounds.Contains);

        var effect = new ColorShift { RedOffset = { CurrentValue = new PixelPoint(20, 0) } };

        // maxDimension = 60 clamps the 120-px forward-inflated ColorShift buffer to w = 0.5 (below describe w = 1.0).
        using Bitmap output = RenderSingleEffectClamped(effect, input, inputBounds, boundaryScale: 1f, maxDimension: 60);

        int edgeX = RightmostRedEdge(output, y: 50);
        Assert.That(edgeX, Is.EqualTo(60).Within(6),
            $"the red channel is shifted by 20 logical px at the EXECUTION density (edge at 60 px); a describe-time "
            + $"bake over-shifts to ~80 px. Observed edge {edgeX}.");
    }

    // Seam green-lock: a test-authored whole-source shader binds a Deferred uniform reading ctx.TargetWidth. Under a
    // forward-inflated buffer clamped to w=0.5 (60-px device buffer), the uniform must reflect the ACTUAL 60-px
    // target, placing the opaque edge at logical 60. A describe-time size would exceed the real buffer and fill it.
    [Test]
    public void WholeSourceShader_DeferredUniform_ReadsExecutionTargetSize()
    {
        var inputBounds = new Rect(0, 0, 100, 100);
        var descriptor = ShaderNodeDescriptor.WholeSource(
            """
            uniform shader src;
            uniform float probe;
            half4 main(float2 c) {
                return c.x < probe ? half4(1.0) : half4(0.0);
            }
            """,
            BoundsContract.Create(static r => r.Inflate(new Thickness(0, 0, 20, 0)), static r => r),
            u => u.Deferred("probe", static (b, name, ctx) => b.Uniforms[name] = ctx.TargetWidth * 0.5f));

        var builder = new EffectGraphBuilder(inputBounds, outputScale: 1f, workingScale: 1f);
        builder.Shader(descriptor);
        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f, maxDimension: 60);
        Assert.That(frame.Passes[0].WorkingScale, Is.EqualTo(0.5f).Within(1e-4f), "sanity: the inflated buffer clamps to w=0.5");

        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [OpaqueInput(inputBounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool);
        try
        {
            Rect outBounds = ops[0].Bounds;
            var size = PixelRect.FromRect(outBounds);
            using RenderTarget target = RenderTarget.Create(size.Width, size.Height)!;
            using (var canvas = new ImmediateCanvas(target, 1f, logicalSize: outBounds.Size))
            {
                canvas.Clear();
                using (canvas.PushTransform(Matrix.CreateTranslation(-outBounds.X, -outBounds.Y)))
                    ops[0].Render(canvas);
            }

            using Bitmap bmp = target.Snapshot();
            int edgeX = RightmostRedEdge(bmp, y: 50);
            Assert.That(edgeX, Is.EqualTo(60).Within(6),
                $"the Deferred uniform read the execution-time 60-px buffer (opaque edge at logical 60). Observed {edgeX}.");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    // F2: a DisplacementMap whose pass executes below its describe density must sample the map at the EXECUTION
    // density. The map is a half-plane (opaque for logical x < 50, transparent for x >= 50); the translate transform
    // pulls the source band up by 40 logical px only where the map is opaque. maxDimension = 50 clamps the 100-px
    // displacement buffer to w = 0.5 (below describe w = 1.0). Post-fix the deferred child rebuilds the map at w = 0.5,
    // so the opaque/transparent boundary lands at logical 50. Pre-fix the describe-w bake reads only the map's left
    // half across the whole device buffer (disp = 1 everywhere), displacing the band full-width (edge runs to ~100).
    [Test]
    public void DisplacementMap_ForwardInflatedBelowDescribeDensity_MapLookupLandsAtCorrectLogicalPosition()
    {
        var inputBounds = new Rect(0, 0, 100, 100);
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            inputBounds,
            canvas => canvas.DrawRectangle(new Rect(0, 50, 100, 20), Brushes.Resource.Red, null),
            hitTest: inputBounds.Contains);

        var map = new LinearGradientBrush();
        map.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        map.EndPoint.CurrentValue = new RelativePoint(1, 0, RelativeUnit.Relative);
        map.GradientStops.Add(new GradientStop(Colors.White, 0));
        map.GradientStops.Add(new GradientStop(Colors.White, 0.5f));
        map.GradientStops.Add(new GradientStop(Colors.Transparent, 0.5f));
        map.GradientStops.Add(new GradientStop(Colors.Transparent, 1));

        var effect = new DisplacementMapEffect
        {
            Channel = { CurrentValue = DisplacementMapChannel.Alpha },
            Signed = { CurrentValue = false },
            DisplacementMap = { CurrentValue = map },
            Transform =
            {
                CurrentValue = new DisplacementMapTranslateTransform
                {
                    X = { CurrentValue = 0 },
                    Y = { CurrentValue = 40 },
                },
            },
        };

        using Bitmap output = RenderSingleEffectClamped(effect, input, inputBounds, boundaryScale: 1f, maxDimension: 50);

        int edgeX = RightmostRedEdge(output, y: 20);
        Assert.That(edgeX, Is.EqualTo(50).Within(8),
            $"the displacement map is sampled at the EXECUTION density (opaque edge at logical 50); a describe-time "
            + $"bake mis-scales the lookup and displaces the full width (~100). Observed edge {edgeX}.");
    }

    // F2 seam (GPU-backed CPU raster): a Deferred child factory MUST receive the pass's execution-time
    // PassUniformContext (the re-clamped w and buffer size, not describe w = 1), and the executor MUST dispose its
    // per-pass product after the draw (no leak). The forward-inflated 120-px buffer clamps to w = 0.5 (60-px device).
    [Test]
    public void WholeSourceShader_DeferredChild_ReceivesExecutionContextAndDisposesProductAfterPass()
    {
        var inputBounds = new Rect(0, 0, 100, 100);
        PassUniformContext seen = default;
        SKShader? produced = null;
        var descriptor = ShaderNodeDescriptor.WholeSource(
            """
            uniform shader src;
            uniform shader map;
            half4 main(float2 c) { return map.eval(c); }
            """,
            BoundsContract.Create(static r => r.Inflate(new Thickness(0, 0, 20, 0)), static r => r),
            children:
            [
                ChildBinding.Deferred("map", ctx =>
                {
                    seen = ctx;
                    produced = SKShader.CreateColor(new SKColor(200, 50, 60, 255));
                    return produced;
                }),
            ]);

        var builder = new EffectGraphBuilder(inputBounds, outputScale: 1f, workingScale: 1f);
        builder.Shader(descriptor);
        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f, maxDimension: 60);
        Assert.That(frame.Passes[0].WorkingScale, Is.EqualTo(0.5f).Within(1e-4f), "sanity: the inflated buffer clamps to w=0.5");

        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [OpaqueInput(inputBounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(produced, Is.Not.Null, "the deferred child factory ran during execution");
                Assert.That(seen.WorkingScale, Is.EqualTo(0.5f).Within(1e-4f),
                    "the factory saw the execution density, not describe w=1");
                Assert.That(seen.TargetWidth, Is.EqualTo(60), "the factory saw the execution buffer width");
            });
            Assert.That(produced!.Handle, Is.EqualTo(nint.Zero),
                "the executor disposed the per-pass deferred child product after the draw");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    // F2 unit seam (no GPU): the Deferred factory resolves as executor-owned and receives the exact context; an eager
    // child resolves as graph-/caller-owned so the executor never disposes it.
    [Test]
    public void ChildBinding_Deferred_ResolvesExecutorOwnedWithContext_EagerStaysCallerOwned()
    {
        var ctx = new PassUniformContext(0.5f, 60, 40);

        PassUniformContext seen = default;
        using SKShader deferredProduct = SKShader.CreateColor(SKColors.Red);
        ChildBinding deferred = ChildBinding.Deferred("m", c =>
        {
            seen = c;
            return deferredProduct;
        });
        SKShader resolvedDeferred = deferred.Resolve(in ctx, out bool deferredOwned);
        Assert.Multiple(() =>
        {
            Assert.That(deferredOwned, Is.True, "a deferred child's product is executor-owned (disposed per pass)");
            Assert.That(resolvedDeferred, Is.SameAs(deferredProduct));
            Assert.That(seen, Is.EqualTo(ctx), "the factory receives the execution PassUniformContext");
            Assert.That(deferred.Shader, Is.Null, "a deferred binding has no describe-time shader");
        });

        using SKShader eagerShader = SKShader.CreateColor(SKColors.Blue);
        var eager = new ChildBinding("m", eagerShader);
        SKShader resolvedEager = eager.Resolve(in ctx, out bool eagerOwned);
        Assert.Multiple(() =>
        {
            Assert.That(eagerOwned, Is.False, "an eager child is graph-/caller-owned; the executor must not dispose it");
            Assert.That(resolvedEager, Is.SameAs(eagerShader));
        });
    }

    // A same-source (same-signature) shader run reuses the cached SKRuntimeShaderBuilder across executions. A second
    // run that OMITS a uniform the first run bound must see the program default, not the first run's stale value — the
    // executor resets the reused builder's uniforms/children before rebinding. Pre-fix the omitted uniform inherited
    // the stale value (and an omitted executor-owned child would reference a shader disposed after the first draw).
    [Test]
    public void ReusedProgramBuilder_OmittedUniform_SeesDefaultNotStaleValue()
    {
        ProgramCache.Clear();
        var inputBounds = new Rect(0, 0, 40, 40);
        const string source =
            """
            uniform shader src;
            uniform float probe;
            half4 main(float2 c) { return c.x < probe ? half4(1.0, 0.0, 0.0, 1.0) : half4(0.0, 0.0, 1.0, 1.0); }
            """;

        // First run binds probe large: c.x (< 40) < 1000 everywhere, so the buffer is red.
        using Bitmap first = RenderWholeSource(source, inputBounds, u => u.Float("probe", 1000f));
        Assert.That(first.SKBitmap.GetPixel(20, 20).Red, Is.GreaterThan(128),
            "sanity: the first run binds probe=1000 (red everywhere)");

        // Second run reuses the cached builder but omits probe: it must default to 0, so c.x < 0 is false (blue).
        using Bitmap second = RenderWholeSource(source, inputBounds, uniforms: null);
        SKColor px = second.SKBitmap.GetPixel(20, 20);
        Assert.Multiple(() =>
        {
            Assert.That(px.Blue, Is.GreaterThan(128),
                "the omitted probe reads the program default (0 -> blue); a stale bleed keeps the first run's red");
            Assert.That(px.Red, Is.LessThan(128),
                "the reused builder must not carry the first run's probe=1000");
        });
    }

    // Renders a non-invariant whole-source shader (its own FusedShaderPass, so it flows through BuildRuntimeRun and
    // the ProgramCache) over an opaque input, then rasterizes the output op back to logical space.
    private static Bitmap RenderWholeSource(
        string source, Rect inputBounds, Action<UniformBindingBuilder>? uniforms)
    {
        var descriptor = ShaderNodeDescriptor.WholeSource(source, BoundsContract.Identity, uniforms);
        var builder = new EffectGraphBuilder(inputBounds, outputScale: 1f, workingScale: 1f);
        builder.Shader(descriptor);
        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [OpaqueInput(inputBounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool);
        try
        {
            Rect outBounds = ops[0].Bounds;
            var size = PixelRect.FromRect(outBounds);
            using RenderTarget target = RenderTarget.Create(size.Width, size.Height)!;
            using (var canvas = new ImmediateCanvas(target, 1f, logicalSize: outBounds.Size))
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

    // Renders a single effect over an input, resolving its buffer at an overridden budget so a forward-inflated pass
    // executes below the describe-time density, then rasterizes the output op back to logical space.
    private static Bitmap RenderSingleEffectClamped(
        FilterEffect effect, RenderNodeOperation input, Rect inputBounds, float boundaryScale, int maxDimension)
    {
        var builder = new EffectGraphBuilder(inputBounds, outputScale: 1f, workingScale: boundaryScale);
        effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default));

        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, boundaryScale, maxDimension);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: boundaryScale,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool);
        try
        {
            Rect outBounds = ops[0].Bounds;
            var size = PixelRect.FromRect(outBounds);
            using RenderTarget target = RenderTarget.Create(size.Width, size.Height)!;
            using (var canvas = new ImmediateCanvas(target, 1f, logicalSize: outBounds.Size))
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

    // The rightmost x on the given row whose red channel is high (white region) while the pixel is opaque.
    private static int RightmostRedEdge(Bitmap bmp, int y)
    {
        int edge = -1;
        for (int x = 0; x < bmp.Width; x++)
        {
            SkiaSharp.SKColor px = bmp.SKBitmap.GetPixel(x, y);
            if (px.Alpha > 128 && px.Red > 128)
                edge = x;
        }

        return edge;
    }

    private static RenderNodeOperation OpaqueInput(Rect bounds)
        => RenderNodeOperation.CreateLambda(
            bounds,
            canvas => canvas.DrawRectangle(bounds, Brushes.Resource.White, null),
            hitTest: bounds.Contains);
}

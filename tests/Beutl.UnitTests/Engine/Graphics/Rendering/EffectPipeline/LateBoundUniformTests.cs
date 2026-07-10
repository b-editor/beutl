using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression cover for finding F1 (feature 004): a density-/size-dependent shader uniform frozen at the
/// describe-time <see cref="EffectGraphBuilder.WorkingScale"/> is wrong when the resource-resolution re-clamp
/// (<c>ClampWorkingScaleToBufferBudget</c>) executes the pass BELOW that density. The uniform must be late-bound
/// to the pass's real <see cref="PassUniformContext"/> (via <c>DensityScaledFloat2</c> / <c>Deferred</c>).
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

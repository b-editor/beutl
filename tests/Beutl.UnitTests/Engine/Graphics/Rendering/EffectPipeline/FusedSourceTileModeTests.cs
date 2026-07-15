using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// A whole-source pass's declared src tile mode must apply at the SOURCE footprint edge (the legacy custom effect
/// tiled the source's own snapshot). Tile modes only act outside the sampled image, so when a pass grows its output
/// the in-place bake padded the image with transparency and Clamp/Repeat/Mirror never engaged at the real edge —
/// a clamp sample beyond the source returned transparent instead of the edge pixel, breaking
/// DisplacementMap-style warps with a non-Decal <c>SpreadMethod</c>. Raster, GPU-less.
/// </summary>
[NonParallelizable]
[TestFixture]
public class FusedSourceTileModeTests
{
    private static readonly Rect s_source = new(0, 0, 8, 8);

    private const string IdentitySource =
        "uniform shader src;\nhalf4 main(float2 coord){ return src.eval(coord); }";

    [TestCase(false)]
    [TestCase(true)]
    public void SkslScriptEffect_UsesLegacyClampTileMode(bool coordinateInvariant)
    {
        var effect = new SKSLScriptEffect();
        effect.CoordinateInvariant.CurrentValue = coordinateInvariant;
        using FilterEffect.Resource resource =
            (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(s_source, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        effect.Describe(builder, resource);

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        var pass = (FusedShaderPass)plan.Passes.Single();
        var stage = (RuntimeShaderStage)pass.Stages.Single();

        Assert.That(stage.SrcTileMode, Is.EqualTo(SKShaderTileMode.Clamp),
            "SKSLScriptEffect must preserve the legacy SKImage.ToShader() Clamp/Clamp source-edge behavior");
    }

    [Test]
    public void GrowingWholeSource_ClampTileMode_ExtendsTheSourceEdgePixel()
    {
        RenderNodeOperation[] ops = Execute(SKShaderTileMode.Clamp);
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1));
            Rect bounds = ops[0].Bounds;
            Assert.That(bounds, Is.EqualTo(s_source.Inflate(4)), "the forward contract grows the output by 4");

            using Bitmap bmp = Rasterize(ops[0], bounds);
            Assert.Multiple(() =>
            {
                Assert.That(PixelAt(bmp, bounds, new Point(-2, 4)).Alpha, Is.Not.Zero,
                    "a clamp sample beyond the source edge must return the edge pixel, not the transparent padding");
                Assert.That(PixelAt(bmp, bounds, new Point(4, 4)).Red, Is.Not.Zero,
                    "the source interior passes through");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    // Control: Decal keeps the frozen-reference behavior — beyond-source samples stay transparent — and guards the
    // clamp assertion above against measuring the wrong pixel.
    [Test]
    public void GrowingWholeSource_DecalTileMode_KeepsTransparentPadding()
    {
        RenderNodeOperation[] ops = Execute(SKShaderTileMode.Decal);
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1));
            using Bitmap bmp = Rasterize(ops[0], ops[0].Bounds);
            Assert.That(PixelAt(bmp, ops[0].Bounds, new Point(-2, 4)).Alpha, Is.Zero,
                "a decal sample beyond the source stays transparent");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    [Test]
    public void WholeSourceHaloCleanupFailure_ReleasesCompletedOutputAndInputOperation()
    {
        using var pool = new RenderTargetPool();
        var injected = new InvalidOperationException("source halo cleanup failed");
        int disposeCount = 0;
        pool.SetDisposeBackingForTest(pooled =>
        {
            pooled.DisposeBacking();
            if (disposeCount++ == 0)
                throw injected;
        });

        bool inputDisposed = false;
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_source,
            canvas => canvas.DrawRectangle(s_source, Brushes.Resource.Red, null),
            onDispose: () => inputDisposed = true);
        var builder = new EffectGraphBuilder(s_source, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Shader(ShaderNodeDescriptor.WholeSource(
            """
            uniform shader src;
            uniform float trigger;
            half4 main(float2 coord) { return src.eval(coord) * trigger; }
            """,
            BoundsContract.Create(static r => r.Inflate(4), static r => r.Inflate(4)),
            uniforms: uniforms => uniforms.Deferred("trigger", (runtime, name, _) =>
            {
                // Both the output and separate source-halo targets are live here. Disposing the pool makes their
                // subsequent final returns exercise the native-teardown failure path without a production seam.
                pool.Dispose();
                runtime.Uniforms[name] = 1f;
            }),
            srcTileMode: SKShaderTileMode.Clamp));
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(injected));
            Assert.That(inputDisposed, Is.True,
                "the source operation must be consumed when source-halo cleanup fails after a successful draw");
            Assert.That(pool.LiveLeaseCount, Is.Zero,
                "the completed output must not remain leased when source-halo cleanup fails");
        });
    }

    [Test]
    public void WholeSourceHaloAllocationFailure_PreviewDropsAfterOutputCleanupFailure()
    {
        using var pool = new RenderTargetPool();
        var cleanup = new InvalidOperationException("descriptor output cleanup failed");
        pool.SetDisposeBackingForTest(pooled =>
        {
            pooled.DisposeBacking();
            throw cleanup;
        });
        int acquireCount = 0;
        pool.SetBackingFactoryForTest((width, height) =>
        {
            if (acquireCount++ == 0)
                return RenderTarget.CreateBackingSurface(width, height);

            // The output target is still live. Marking the pool disposed makes its best-effort return exercise the
            // native-teardown failure path after the source-halo allocation reports null.
            pool.Dispose();
            return null;
        });

        bool inputDisposed = false;
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_source,
            canvas => canvas.DrawRectangle(s_source, Brushes.Resource.Red, null),
            onDispose: () => inputDisposed = true);
        var builder = new EffectGraphBuilder(s_source, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Preview);
        builder.Shader(ShaderNodeDescriptor.WholeSource(
            IdentitySource,
            BoundsContract.Create(static r => r.Inflate(4), static r => r.Inflate(4)),
            srcTileMode: SKShaderTileMode.Clamp));
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);

        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: 2f, diagnostics: null, pool: pool, renderIntent: RenderIntent.Preview);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(outputs, Is.Empty,
                    "preview must keep the source-halo allocation-failure drop contract when output cleanup faults");
                Assert.That(inputDisposed, Is.True, "the source operation must still be consumed");
                Assert.That(pool.LiveLeaseCount, Is.Zero, "the descriptor output must not remain leased");
            });
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    private static RenderNodeOperation[] Execute(SKShaderTileMode tileMode)
    {
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_source,
            canvas => canvas.DrawRectangle(s_source, Brushes.Resource.Red, null),
            hitTest: s_source.Contains);

        var builder = new EffectGraphBuilder(s_source, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Shader(ShaderNodeDescriptor.WholeSource(
            IdentitySource,
            BoundsContract.Create(static r => r.Inflate(4), static r => r.Inflate(4)),
            srcTileMode: tileMode));
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        return PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
    }

    private static SKColor PixelAt(Bitmap bmp, Rect window, Point logical)
        => bmp.SKBitmap.GetPixel((int)(logical.X - window.X), (int)(logical.Y - window.Y));

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

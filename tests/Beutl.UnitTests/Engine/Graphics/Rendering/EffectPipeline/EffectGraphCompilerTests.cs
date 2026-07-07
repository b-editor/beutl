using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Covers the declarative effect-graph compiler, its per-frame resource resolution, and the fused shader
/// executor (feature 004, T029). Uses synthetic test-authored descriptors via the public builder surface;
/// the fused-execution cases run on the raster backend (no Vulkan) so they are GPU-less-CI-safe.
/// </summary>
[TestFixture]
public class EffectGraphCompilerTests
{
    // A coordinate-invariant snippet that scales premultiplied rgb by a uniform. Distinct uniform name per role
    // keeps the snippet merger's whole-word prefixing unambiguous.
    private const string ScaleSnippet =
        """
        uniform float scaleAmount;
        half4 apply(half4 c) {
            return half4(c.rgb * scaleAmount, c.a);
        }
        """;

    private static ShaderNodeDescriptor Scale(float amount)
        => ShaderNodeDescriptor.Snippet(ScaleSnippet, u => u.Float("scaleAmount", amount));

    private static EffectGraphBuilder NewBuilder(Rect bounds, float workingScale = 1f)
        => new(bounds, outputScale: 1f, workingScale: workingScale);

    private static CompiledPlan Compile(EffectGraphBuilder builder)
    {
        using EffectGraph graph = builder.Build();
        return EffectGraphCompiler.Compile(graph, diagnostics: null);
    }

    // ---- Fusion grouping --------------------------------------------------------------------------------

    [Test]
    public void Compile_MaximalInvariantRun_CollapsesToOneFusedPass()
    {
        var bounds = new Rect(0, 0, 100, 80);
        EffectGraphBuilder builder = NewBuilder(bounds)
            .Shader(Scale(0.9f))
            .Shader(Scale(0.8f))
            .ColorFilter(ColorFilterNodeDescriptor.Create(() => SKColorFilter.CreateLumaColor(), "Luma"))
            .Shader(Scale(1.1f));

        CompiledPlan plan = Compile(builder);

        Assert.That(plan.Passes, Has.Length.EqualTo(1));
        Assert.That(plan.Passes[0], Is.TypeOf<FusedShaderPass>());
        Assert.That(((FusedShaderPass)plan.Passes[0]).Stages, Has.Length.EqualTo(4),
            "all four adjacent invariant nodes fuse into one pass (maximality)");
    }

    [Test]
    public void Compile_ExceedingStageBudget_SplitsIntoConsecutiveFusedPasses()
    {
        var bounds = new Rect(0, 0, 100, 80);
        EffectGraphBuilder builder = NewBuilder(bounds);
        int nodeCount = EffectGraphCompiler.MaxFusionStages + 1;
        for (int i = 0; i < nodeCount; i++)
            builder.Shader(Scale(1f));

        CompiledPlan plan = Compile(builder);

        Assert.That(plan.Passes, Has.Length.EqualTo(2), "17 invariant nodes split at the 16-stage budget");
        Assert.That(((FusedShaderPass)plan.Passes[0]).Stages, Has.Length.EqualTo(EffectGraphCompiler.MaxFusionStages));
        Assert.That(((FusedShaderPass)plan.Passes[1]).Stages, Has.Length.EqualTo(1));
    }

    [Test]
    public void Compile_FusionNeverCrossesASkiaFilter()
    {
        var bounds = new Rect(0, 0, 100, 80);
        EffectGraphBuilder builder = NewBuilder(bounds)
            .Shader(Scale(1f))
            .Blur(new Size(4, 4))
            .Shader(Scale(1f));

        CompiledPlan plan = Compile(builder);

        Assert.That(plan.Passes.Select(p => p.GetType()),
            Is.EqualTo(new[] { typeof(FusedShaderPass), typeof(SkiaFilterPass), typeof(FusedShaderPass) }));
    }

    // ---- Structural key vs parameters -------------------------------------------------------------------

    [Test]
    public void StructuralKey_ParameterOnlyChange_IsEqual_ButSizesReResolve()
    {
        var bounds = new Rect(0, 0, 200, 150);

        CompiledPlan planSmall = Compile(NewBuilder(bounds).Shader(Scale(0.5f)).Blur(new Size(5, 5)));
        CompiledPlan planLarge = Compile(NewBuilder(bounds).Shader(Scale(0.9f)).Blur(new Size(20, 20)));

        Assert.That(planSmall.Key, Is.EqualTo(planLarge.Key),
            "a uniform value and an animated blur sigma are parameters — the structural key must match so a cache hits");

        FrameResources small = EffectGraphCompiler.ResolveResources(planSmall, Rect.Invalid, workingScale: 1f);
        FrameResources large = EffectGraphCompiler.ResolveResources(planLarge, Rect.Invalid, workingScale: 1f);

        // The blur pass (index 1) inflates by sigma×3, so its resolved buffer grows with sigma — recompiled? no,
        // re-resolved: the plans differ only in per-frame sizes, proving bounds are not part of the key.
        Assert.That(large.Passes[1].Width, Is.GreaterThan(small.Passes[1].Width));
        Assert.That(large.Passes[1].Height, Is.GreaterThan(small.Passes[1].Height));
    }

    [Test]
    public void StructuralKey_DifferentEffectKind_Differs()
    {
        var bounds = new Rect(0, 0, 100, 100);
        CompiledPlan blur = Compile(NewBuilder(bounds).Blur(new Size(5, 5)));
        CompiledPlan dilate = Compile(NewBuilder(bounds).Dilate(5, 5));

        Assert.That(blur.Key, Is.Not.EqualTo(dilate.Key));
    }

    // ---- Resource plan (peak-live) ----------------------------------------------------------------------

    [Test]
    public void ResourcePlan_LinearChain_PeakLiveIsTwo_IndependentOfLength()
    {
        var bounds = new Rect(0, 0, 100, 100);

        CompiledPlan four = Compile(NewBuilder(bounds)
            .Shader(Scale(1f)).Blur(new Size(2, 2)).Shader(Scale(1f)).Blur(new Size(2, 2)));
        CompiledPlan eight = Compile(NewBuilder(bounds)
            .Shader(Scale(1f)).Blur(new Size(2, 2)).Shader(Scale(1f)).Blur(new Size(2, 2))
            .Shader(Scale(1f)).Blur(new Size(2, 2)).Shader(Scale(1f)).Blur(new Size(2, 2)));

        Assert.That(four.Passes, Has.Length.EqualTo(4));
        Assert.That(eight.Passes, Has.Length.EqualTo(8));
        Assert.That(four.Resources.PeakLiveCount, Is.EqualTo(2));
        Assert.That(eight.Resources.PeakLiveCount, Is.EqualTo(2),
            "double-buffer bound: a longer linear chain does not raise peak-live intermediates (FR-007)");

        // Intervals: pass i writes decl i, consumed by pass i+1 (the tail's output is the frame result).
        ImmutableArray<IntermediateDecl> decls = eight.Resources.Intermediates;
        for (int i = 0; i < decls.Length; i++)
        {
            Assert.That(decls[i].FirstUse, Is.EqualTo(i));
            Assert.That(decls[i].LastUse, Is.EqualTo(i < decls.Length - 1 ? i + 1 : i));
        }
    }

    // ---- ROI backward propagation -----------------------------------------------------------------------

    [Test]
    public void ResolveResources_BackwardRoi_InflatesUpstreamFromRequestedRegion()
    {
        var bounds = new Rect(0, 0, 400, 400);
        // Fused (identity) pass, then a Skia pass whose backward inflates the required input by 20 on each side.
        var inflatingSkia = SkiaFilterNodeDescriptor.Create(
            static inner => inner,
            BoundsContract.Create(static r => r, static r => r.Inflate(20)),
            structuralToken: "InflateBackward");
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)).SkiaFilter(inflatingSkia));

        var requested = new Rect(150, 150, 100, 100);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, requested, workingScale: 1f);

        // Skia pass ROI == requested; fused pass ROI == requested inflated by 20 (clamped to the fused bounds).
        Assert.That(res.Passes[1].OutputRoi, Is.EqualTo(requested));
        Assert.That(res.Passes[0].Width, Is.EqualTo(140), "100 + 2×20 backward inflation");
        Assert.That(res.Passes[0].Height, Is.EqualTo(140));
    }

    [Test]
    public void ResolveResources_DropShadowBackward_CoversSourceAndShadowRegion()
    {
        var bounds = new Rect(0, 0, 400, 400);
        // DropShadow at position (30, 0), sigma 0: output region r needs input r ∪ (r − position).
        CompiledPlan plan = Compile(NewBuilder(bounds)
            .Shader(Scale(1f))
            .DropShadow(new Point(30, 0), new Size(0, 0), Colors.Black));

        var requested = new Rect(150, 150, 100, 100);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, requested, workingScale: 1f);

        // Upstream ROI = (150..250) ∪ (120..220) on x = (120, 150, 130, 100); identity backward would clip
        // the shadow's source pixels at x ∈ [120, 150).
        Assert.That(res.Passes[0].OutputRoi, Is.EqualTo(new Rect(120, 150, 130, 100)));
        Assert.That(res.Passes[0].Width, Is.EqualTo(130));
        Assert.That(res.Passes[0].Height, Is.EqualTo(100));
    }

    [Test]
    public void ResolveResources_TransformBackward_MapsRoiThroughInverseMatrix()
    {
        var bounds = new Rect(0, 0, 400, 400);
        CompiledPlan plan = Compile(NewBuilder(bounds)
            .Shader(Scale(1f))
            .Transform(Matrix.CreateScale(2f, 2f), BitmapInterpolationMode.Default));

        var requested = new Rect(200, 200, 200, 200);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, requested, workingScale: 1f);

        // The requested 200×200 output region under a 2× scale reads only a 100×100 input region; identity
        // backward would over-request (and mis-place) the upstream crop.
        Assert.That(res.Passes[1].OutputRoi, Is.EqualTo(requested));
        Assert.That(res.Passes[0].OutputRoi, Is.EqualTo(new Rect(100, 100, 100, 100)));
        Assert.That(res.Passes[0].Width, Is.EqualTo(100));
    }

    [Test]
    public void ResolveResources_NonInvertibleTransform_FallsBackToFullUpstreamBounds()
    {
        var bounds = new Rect(0, 0, 400, 400);
        CompiledPlan plan = Compile(NewBuilder(bounds)
            .Shader(Scale(1f))
            .Transform(Matrix.CreateScale(0f, 0f), BitmapInterpolationMode.Default));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);

        // A singular matrix has no inverse: backward returns Rect.Invalid and the upstream pass resolves to
        // its full bounds (safe fallback); the degenerate transform output itself is skipped as empty.
        Assert.That(res.Passes[0].Width, Is.EqualTo(400));
        Assert.That(res.Passes[0].Height, Is.EqualTo(400));
        Assert.That(res.Passes[1].SkipEmpty, Is.True);
    }

    [Test]
    public void ResolveResources_RenderTimePass_FallsBackToFullInputBounds()
    {
        var bounds = new Rect(0, 0, 300, 200);
        var renderTime = ShaderNodeDescriptor.WholeSource(
            "uniform shader src; half4 main(float2 coord){ return src.eval(coord); }",
            BoundsContract.Create(static r => r, static r => r, isRenderTimeResolved: true));
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(renderTime));

        // A small requested region cannot narrow a render-time pass: it uses the full input bounds.
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, new Rect(10, 10, 20, 20), workingScale: 1f);

        Assert.That(res.Passes[0].Width, Is.EqualTo(300));
        Assert.That(res.Passes[0].Height, Is.EqualTo(200));
    }

    [Test]
    public void ResolveResources_EmptyRoi_FlagsPassSkip()
    {
        var bounds = new Rect(0, 0, 100, 100);
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)));

        // A requested region disjoint from the effect bounds resolves to an empty ROI -> runtime skip.
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, new Rect(500, 500, 50, 50), workingScale: 1f);

        Assert.That(res.Passes[0].SkipEmpty, Is.True);
    }

    // The flag test above only proves the resolver marks the pass; this proves the executor's behavior on the flag:
    // a pass whose resolved OUTPUT is empty (a shrinking pass, e.g. a fully-closed Clipping) must produce nothing,
    // not pass the still-present input through (legacy Clipping.Apply removed the target on an empty intersect).
    [Test]
    public void Execute_ShrinkingPassToEmptyOutput_DropsOutput_NotInputPassThrough()
    {
        var bounds = new Rect(0, 0, 100, 100);
        bool callbackRan = false;
        var closing = GeometryNodeDescriptor.Create(
            _ => callbackRan = true,
            BoundsContract.Create(static _ => Rect.Empty, static r => r),
            structuralToken: "CloseToEmpty");

        RenderNodeOperation[] outputs = Execute(
            NewBuilder(bounds).Geometry(closing), bounds, [MakeInput(bounds)], diagnostics: null, pool: null);

        Assert.That(outputs, Is.Empty,
            "an empty resolved output drops the pass result; returning the input would leak a full-size image");
        Assert.That(callbackRan, Is.False, "the geometry callback never runs for an empty output");
    }

    // The other skip cause: the INPUT op is itself empty. A coordinate-invariant identity pass over nothing is
    // nothing, so the empty op passes straight through unchanged (no crash, no phantom buffer).
    [Test]
    public void Execute_EmptyInputToInvariantPass_PassesEmptyOpThrough()
    {
        var bounds = new Rect(0, 0, 100, 100);
        RenderNodeOperation empty = MakeInput(new Rect(10, 10, 0, 0));

        RenderNodeOperation[] outputs = Execute(
            NewBuilder(bounds).Shader(Scale(1f)), bounds, [empty], diagnostics: null, pool: null);

        Assert.That(outputs, Has.Length.EqualTo(1), "an empty input to an identity pass survives as an empty op");
        Assert.That(outputs[0].Bounds.Width * outputs[0].Bounds.Height, Is.EqualTo(0));
        RenderNodeOperation.DisposeAll(outputs);
    }

    // ---- Working-scale carry + 16384 clamp --------------------------------------------------------------

    [Test]
    public void ResolveResources_NoClampNeeded_KeepsWorkingScale()
    {
        var bounds = new Rect(0, 0, 100, 100);
        var identitySkia = SkiaFilterNodeDescriptor.Create(
            static inner => inner, BoundsContract.Create(static r => r, static r => r), "Identity");
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)).SkiaFilter(identitySkia));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 2f);

        Assert.That(res.Passes, Has.Length.EqualTo(2));
        Assert.That(res.Passes[0].WorkingScale, Is.EqualTo(2f));
        Assert.That(res.Passes[1].WorkingScale, Is.EqualTo(2f));
    }

    [Test]
    public void ResolveResources_OversizedChain_ClampsAndCarriesWorkingScaleMonotonically()
    {
        // 10000 px axis at w=2 -> 20000 px buffer, over the 16384 axis budget: the clamp fires and carries.
        var bounds = new Rect(0, 0, 10000, 10000);
        var identitySkia = SkiaFilterNodeDescriptor.Create(
            static inner => inner, BoundsContract.Create(static r => r, static r => r), "Identity");
        CompiledPlan plan = Compile(NewBuilder(bounds).SkiaFilter(identitySkia).Shader(Scale(1f)));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 2f);

        float expected = RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, 2f);
        Assert.That(expected, Is.LessThan(2f), "sanity: this chain must trigger the clamp");
        Assert.That(res.Passes[0].WorkingScale, Is.EqualTo(expected));
        Assert.That(res.Passes[1].WorkingScale, Is.LessThanOrEqualTo(res.Passes[0].WorkingScale),
            "the reduced working scale carries monotonically to downstream passes (legacy Flush parity)");
        Assert.That(res.Passes[0].Width, Is.LessThanOrEqualTo(RenderNodeContext.MaxBufferDimension));
    }

    // MosaicEffect computes its resolution/tileSize/origin uniforms at describe time from builder.WorkingScale.
    // At the 16384 px/axis budget edge this matches the executed buffer only because the node-level clamp
    // (FilterEffectRenderNode.Process) runs BEFORE Describe and the pass has identity bounds, so the per-pass
    // re-clamp in ResolveResources lands on the same w. Pins that equality at a clamping size.
    [Test]
    public void MosaicEffect_AtBufferBudgetEdge_DescribeTimeUniformsMatchExecuteTimeBuffer()
    {
        var bounds = new Rect(0, 0, 20000, 50);
        float workingScale = RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, 1f);
        Assert.That(workingScale, Is.LessThan(1f), "sanity: this size must trigger the clamp");

        var effect = new MosaicEffect();
        FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(bounds, outputScale: 1f, workingScale: workingScale);
        effect.Describe(builder, resource);
        (int describeW, int describeH) = RenderNodeContext.DeviceBufferSize(builder.Bounds, builder.WorkingScale);

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale);

        Assert.Multiple(() =>
        {
            Assert.That(res.Passes[0].Width, Is.EqualTo(RenderNodeContext.MaxBufferDimension));
            Assert.That((res.Passes[0].Width, res.Passes[0].Height), Is.EqualTo((describeW, describeH)),
                "the describe-time resolution uniform equals the execute-time clamped buffer");
            Assert.That(res.Passes[0].WorkingScale, Is.EqualTo(workingScale).Within(1e-6f),
                "the per-pass re-clamp resolves the same density the uniforms were computed at");
        });
    }

    // ---- End-to-end fused execution (raster, GPU-less) --------------------------------------------------

    [Test]
    public void FusedChain_ThreeInvariantSnippets_OneGpuPassOneIntermediate_MatchesUnfused()
    {
        var bounds = new Rect(0, 0, 96, 64);
        ShaderNodeDescriptor[] chain = [Scale(0.85f), Scale(1.15f), Scale(0.7f)];

        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();

        using Bitmap fused = RenderChain(chain, bounds, fuse: true, diagnostics, pool);
        using Bitmap unfused = RenderChain(chain, bounds, fuse: false, diagnostics: null, pool: null);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.GpuPasses, Is.EqualTo(1), "a fused run of 3 invariant snippets is one GPU pass");
            Assert.That(diagnostics.PoolAcquires, Is.LessThanOrEqualTo(1), "at most one intermediate target");
            Assert.That(diagnostics.ProgramCreations, Is.EqualTo(1), "the 3 snippets merge into one program");

            double ssim = ImageMetrics.Ssim(unfused, fused);
            double mae = ImageMetrics.MeanAbsoluteError(unfused, fused);
            Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin), $"SSIM {ssim}");
            Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), $"MAE {mae}");
        });
    }

    [Test]
    public void FusedChain_ArrayMatrixAndRawUniforms_BindThroughMergedProgram()
    {
        var bounds = new Rect(0, 0, 96, 64);

        // Snippet 1: a float[] uniform and a RawUniform-written scalar (the open binding seam);
        // snippet 2: a float3x3 uniform from a 2D scale matrix. Total per-channel gain:
        // rgb × (0.8 × 0.9 × 0.5) × diag(0.5, 0.5, 1) = rgb × (0.18, 0.18, 0.36).
        var arrayAndRaw = ShaderNodeDescriptor.Snippet(
            """
            uniform float gains[2];
            uniform float extraGain;
            half4 apply(half4 c) {
                return half4(c.rgb * gains[0] * gains[1] * extraGain, c.a);
            }
            """,
            u => u.FloatArray("gains", [0.8f, 0.9f])
                .Raw("extraGain", static (b, name) => b.Uniforms[name] = 0.5f));
        var matrixTint = ShaderNodeDescriptor.Snippet(
            """
            uniform float3x3 tint;
            half4 apply(half4 c) {
                return half4(half3(tint * c.rgb), c.a);
            }
            """,
            u => u.Matrix3x3("tint", Matrix.CreateScale(0.5f, 0.5f)));
        var equivalent = ShaderNodeDescriptor.Snippet(
            """
            uniform float3 mulv;
            half4 apply(half4 c) {
                return half4(c.rgb * mulv, c.a);
            }
            """,
            u => u.Float3("mulv", 0.18f, 0.18f, 0.36f));

        var diagnostics = new PipelineDiagnostics();
        using Bitmap fused = RenderChain([arrayAndRaw, matrixTint], bounds, fuse: true, diagnostics, pool: null);
        using Bitmap expected = RenderChain([equivalent], bounds, fuse: false, diagnostics: null, pool: null);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.ProgramCreations, Is.EqualTo(1),
                "the array/raw/matrix snippets merge into one program (prefixing covers array declarations)");

            double ssim = ImageMetrics.Ssim(expected, fused);
            double mae = ImageMetrics.MeanAbsoluteError(expected, fused);
            Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin), $"SSIM {ssim}");
            Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), $"MAE {mae}");
        });
    }

    // Renders a snippet chain over a fixed synthetic input. When fused, the whole chain compiles to one plan;
    // when unfused, each snippet is its own single-node plan run in sequence (the pre-fusion equivalent).
    private static Bitmap RenderChain(
        ShaderNodeDescriptor[] snippets, Rect bounds, bool fuse,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        RenderNodeOperation[] current = [MakeInput(bounds)];
        if (fuse)
        {
            EffectGraphBuilder builder = new(bounds, 1f, 1f);
            foreach (ShaderNodeDescriptor snippet in snippets)
                builder.Shader(snippet);
            current = Execute(builder, bounds, current, diagnostics, pool);
        }
        else
        {
            foreach (ShaderNodeDescriptor snippet in snippets)
            {
                var builder = new EffectGraphBuilder(bounds, 1f, 1f);
                builder.Shader(snippet);
                current = Execute(builder, bounds, current, diagnostics, pool);
            }
        }

        Bitmap result = Rasterize(current[0], bounds);
        current[0].Dispose();
        return result;
    }

    private static RenderNodeOperation[] Execute(
        EffectGraphBuilder builder, Rect bounds, RenderNodeOperation[] inputs,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);
        return PlanExecutor.Execute(
            plan, res, inputs, outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics, pool);
    }

    private static RenderNodeOperation MakeInput(Rect bounds)
    {
        return RenderNodeOperation.CreateLambda(
            bounds,
            canvas =>
            {
                canvas.DrawRectangle(bounds, Brushes.Resource.White, null);
                canvas.DrawRectangle(new Rect(bounds.X, bounds.Y, bounds.Width / 2, bounds.Height), Brushes.Resource.Red, null);
                canvas.DrawRectangle(new Rect(bounds.X, bounds.Y + bounds.Height / 2, bounds.Width, bounds.Height / 2), Brushes.Resource.Blue, null);
            },
            hitTest: bounds.Contains);
    }

    private static Bitmap Rasterize(RenderNodeOperation op, Rect bounds)
    {
        var size = PixelRect.FromRect(bounds);
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null (raster surface unavailable).");
        using (var canvas = new ImmediateCanvas(target, 1f, logicalSize: bounds.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
            {
                op.Render(canvas);
            }
        }

        return target.Snapshot();
    }
}

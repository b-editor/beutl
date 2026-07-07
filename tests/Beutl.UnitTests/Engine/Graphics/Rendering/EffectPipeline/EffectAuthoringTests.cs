using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// The plugin-authoring gate (feature 004, T049, research D7). Every one of the seven descriptor kinds that
/// realize the spec's five primitives is authored here against the <b>public</b> builder/descriptor surface only —
/// the same API an out-of-tree plugin has — and compiled to its expected pass. It also proves SC-006 (a
/// plugin-authored coordinate-invariant snippet fuses between two built-in color effects into one draw) and the A3
/// obligation that a convolution-style node's declared <c>GetRequiredInputBounds</c> covers the region it samples:
/// the compiler-level cases run without a GPU; the crop-vs-full ROI render is Vulkan-gated.
/// </summary>
[TestFixture]
public class EffectAuthoringTests
{
    private static readonly Rect s_bounds = new(0, 0, 128, 96);

    private static EffectGraphBuilder NewBuilder(float workingScale = 1f)
        => new(s_bounds, outputScale: 1f, workingScale: workingScale);

    private static CompiledPlan Compile(EffectGraphBuilder builder)
    {
        using EffectGraph graph = builder.Build();
        return EffectGraphCompiler.Compile(graph, diagnostics: null);
    }

    // A coordinate-invariant snippet a plugin author would write: identity by construction, fusable.
    private const string PassthroughSnippet =
        """
        half4 apply(half4 c) {
            return c;
        }
        """;

    // A whole-source shader that samples only its own pixel but is authored non-invariant (own pass).
    private const string WholeSourcePassthrough =
        """
        uniform shader src;
        half4 main(float2 coord) {
            return src.eval(coord);
        }
        """;

    // ---- One effect per descriptor kind, all via the public authoring surface --------------------------

    [Test]
    public void SnippetShaderNode_CompilesToAFusedPass()
    {
        CompiledPlan plan = Compile(NewBuilder().Shader(ShaderNodeDescriptor.Snippet(PassthroughSnippet)));
        Assert.That(plan.Passes, Has.Length.EqualTo(1));
        Assert.That(plan.Passes[0], Is.TypeOf<FusedShaderPass>());
    }

    [Test]
    public void ColorFilterNode_CompilesToAFusedPass()
    {
        CompiledPlan plan = Compile(NewBuilder()
            .ColorFilter(ColorFilterNodeDescriptor.Create(() => SKColorFilter.CreateLumaColor(), "Luma")));
        Assert.That(plan.Passes[0], Is.TypeOf<FusedShaderPass>());
    }

    [Test]
    public void SkiaFilterNode_CompilesToASkiaFilterPass()
    {
        CompiledPlan plan = Compile(NewBuilder().SkiaFilter(BoxBlur3x3()));
        Assert.That(plan.Passes[0], Is.TypeOf<SkiaFilterPass>());
    }

    [Test]
    public void WholeSourceShaderNode_CompilesToItsOwnPass()
    {
        CompiledPlan plan = Compile(NewBuilder()
            .Shader(ShaderNodeDescriptor.WholeSource(WholeSourcePassthrough, BoundsContract.Identity)));
        // A non-invariant whole-source node is its own single-stage pass (it is not fusable).
        Assert.That(plan.Passes, Has.Length.EqualTo(1));
        Assert.That(plan.Passes[0], Is.TypeOf<FusedShaderPass>());
        Assert.That(((FusedShaderPass)plan.Passes[0]).Stages, Has.Length.EqualTo(1));
    }

    [Test]
    public void ComputeNode_CompilesToAComputePass()
    {
        CompiledPlan plan = Compile(NewBuilder().Compute(
            ComputeNodeDescriptor.Create(_ => { }, passCount: 1, ComputeFallback.Identity, structuralToken: "authoring")));
        Assert.That(plan.Passes[0], Is.TypeOf<ComputePass>());
    }

    [Test]
    public void GeometryNode_CompilesToAGeometryPass()
    {
        CompiledPlan plan = Compile(NewBuilder().Geometry(
            GeometryNodeDescriptor.Create(_ => { }, BoundsContract.Identity, structuralToken: "authoring")));
        Assert.That(plan.Passes[0], Is.TypeOf<GeometryPass>());
    }

    [Test]
    public void SplitAndCompositeNodes_CompileToTheirPassTypes()
    {
        CompiledPlan plan = Compile(NewBuilder()
            .Split(SplitNodeDescriptor.Static(_ => { }, branchCount: 2, structuralToken: "authoring"))
            .Composite(CompositeNodeDescriptor.Create(BlendMode.SrcOver, structuralToken: "authoring")));
        Assert.That(plan.Passes, Has.Length.EqualTo(2));
        Assert.That(plan.Passes[0], Is.TypeOf<SplitPass>());
        Assert.That(plan.Passes[1], Is.TypeOf<CompositePass>());
    }

    // ---- SC-006: an author's invariant snippet fuses between two built-ins ------------------------------

    [Test]
    public void InvariantSnippet_FusesBetweenTwoBuiltins()
    {
        CompiledPlan plan = Compile(NewBuilder()
            .Saturate(1.4f)                                     // built-in ColorFilter
            .Shader(ShaderNodeDescriptor.Snippet(PassthroughSnippet)) // plugin-authored invariant snippet
            .Brightness(1.1f));                                 // built-in ColorFilter

        Assert.That(plan.Passes, Has.Length.EqualTo(1), "the author's snippet fuses with both built-ins");
        Assert.That(plan.Passes[0], Is.TypeOf<FusedShaderPass>());
        Assert.That(((FusedShaderPass)plan.Passes[0]).Stages, Has.Length.EqualTo(3));
    }

    // ---- A3: a convolution node's declared backward ROI covers the region it samples -------------------

    [Test]
    public void ConvolutionNode_DeclaredRoi_InflatesUpstreamByTheKernelRadius()
    {
        // A fused snippet (pass 0) then a 3x3 convolution (pass 1). Requesting a sub-region propagates the
        // convolution's declared backward map to pass 0, which must inflate by the 1px kernel radius (A3).
        CompiledPlan plan = Compile(NewBuilder()
            .Shader(ShaderNodeDescriptor.Snippet(PassthroughSnippet))
            .SkiaFilter(BoxBlur3x3()));

        var requested = new Rect(40, 30, 20, 16);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, requested, workingScale: 1f);

        Rect convolutionRoi = frame.Passes[1].OutputRoi;
        Rect upstreamRoi = frame.Passes[0].OutputRoi;
        Assert.Multiple(() =>
        {
            Assert.That(convolutionRoi, Is.EqualTo(requested), "the tail pass renders exactly the requested region");
            Assert.That(upstreamRoi.Width, Is.GreaterThanOrEqualTo(requested.Width + 2),
                "the upstream ROI inflates by the kernel radius on each side");
            Assert.That(upstreamRoi.Height, Is.GreaterThanOrEqualTo(requested.Height + 2));
            Assert.That(upstreamRoi.Contains(requested), Is.True, "the inflated ROI still covers the request");
        });
    }

    [Test]
    public void ConvolutionNode_DeclaredRoiCrop_MatchesFullInput()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // The declared ROI is the whole input inflated by 1px; cropping the input to it must not change the
            // convolution output versus feeding the full input (the declared region covers every sampled texel).
            Rect declaredInputRoi = s_bounds.Inflate(new Thickness(1, 1, 1, 1));
            using Bitmap cropped = RenderConvolution(cropInputTo: declaredInputRoi.Intersect(s_bounds));
            using Bitmap full = RenderConvolution(cropInputTo: s_bounds);

            double ssim = ImageMetrics.Ssim(full, cropped);
            double mae = ImageMetrics.MeanAbsoluteError(full, cropped);
            TestContext.WriteLine($"declared-ROI-crop vs full: SSIM={ssim:F4} MAE={mae:F4}");
            Assert.Multiple(() =>
            {
                Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin));
                Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax));
            });
        });
    }

    private static Bitmap RenderConvolution(Rect cropInputTo)
    {
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(cropInputTo.Deflate(8), Fill(200, 40, 220, 90), null),
            hitTest: s_bounds.Contains);

        EffectGraphBuilder builder = NewBuilder().SkiaFilter(BoxBlur3x3());
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, frame, [input], s_bounds, outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null);
        return Rasterize(outputs, s_bounds);
    }

    // A 3x3 normalized box blur declared as a Skia matrix-convolution with a symmetric 1px backward inflate.
    private static SkiaFilterNodeDescriptor BoxBlur3x3()
    {
        float[] kernel = new float[9];
        for (int i = 0; i < 9; i++)
            kernel[i] = 1f / 9f;

        var inflate = new Thickness(1, 1, 1, 1);
        return SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateMatrixConvolution(
                new SKSizeI(3, 3), kernel, 1f, 0f, new SKPointI(1, 1),
                SKShaderTileMode.Clamp, convolveAlpha: true, inner),
            BoundsContract.Create(r => r.Inflate(inflate), r => r.Inflate(inflate)),
            structuralToken: "authoring-convolution");
    }

    private static Brush.Resource Fill(byte a, byte r, byte g, byte b)
        => (Brush.Resource)new SolidColorBrush(Color.FromArgb(a, r, g, b))
            .ToResource(Composition.CompositionContext.Default);

    private static Bitmap Rasterize(RenderNodeOperation[] ops, Rect bounds)
    {
        var size = PixelRect.FromRect(bounds);
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using (var canvas = new ImmediateCanvas(target, 1f, logicalSize: bounds.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
            {
                foreach (RenderNodeOperation op in ops)
                    op.Render(canvas);
            }
        }

        RenderNodeOperation.DisposeAll(ops);
        return target.Snapshot();
    }
}

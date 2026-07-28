using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
public sealed class ShaderFallbackTests
{
    private static readonly Rect s_bounds = new(0, 0, 6, 4);

    [TestCase(ShaderDescriptionKind.CurrentPixel)]
    [TestCase(ShaderDescriptionKind.WholeSource)]
    public void OrdinaryCpuBackend_RendersEveryPublicShaderFormWithoutSkipping(
        ShaderDescriptionKind kind)
    {
        using var source = new CpuRenderTarget(6, 4);
        source.Value.Canvas.Clear(new SKColor(64, 128, 192, 160));
        using Bitmap sourceBitmap = source.Snapshot();
        ShaderDescription description = kind == ShaderDescriptionKind.CurrentPixel
            ? ShaderDescription.CurrentPixel(
                "half4 apply(half4 color) { return half4(color.bgr, color.a); }")
            : ShaderDescription.WholeSource(
                "uniform shader src; half4 main(float2 p) { return src.eval(p).bgra; }",
                RenderBoundsContract.Identity);
        using var node = new ShaderNode(source, description);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
                FusionMode = FusionMode.Disabled,
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bitmap, Is.Not.Null);
            Assert.That(rasterization.Bounds, Is.EqualTo(s_bounds));
            Assert.That(SumAbsoluteChannels(rasterization.Bitmap!), Is.GreaterThan(1));
            AssertBlueRedSwap(sourceBitmap, rasterization.Bitmap!);
        });
    }

    [Test]
    public void FusedCurrentPixelStages_ReceiveTheSameRoiCroppedInputBoundsAsUnfusedStages()
    {
        var requestedRegion = new Rect(2, 1, 2, 2);
        using var source = new CpuRenderTarget(6, 4);
        source.Value.Canvas.Clear(new SKColor(64, 128, 192, 160));
        var disabledInputBounds = new List<Rect>();
        var enabledInputBounds = new List<Rect>();
        using var disabledNode = new BoundShaderChainNode(source, disabledInputBounds);
        using var enabledNode = new BoundShaderChainNode(source, enabledInputBounds);
        using var disabled = CreateRenderer(disabledNode, requestedRegion, FusionMode.Disabled);
        using var enabled = CreateRenderer(enabledNode, requestedRegion, FusionMode.Enabled);

        using RenderNodeRasterization disabledRaster = disabled.Rasterize();
        using RenderNodeRasterization enabledRaster = enabled.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(disabledInputBounds[1], Is.EqualTo(requestedRegion));
            Assert.That(enabledInputBounds, Is.EqualTo(disabledInputBounds));
            Assert.That(enabled.LastExecutionStatistics.FusedShaderRunExecutions, Is.EqualTo(1));
            Assert.That(disabledRaster.Bounds, Is.EqualTo(requestedRegion));
            Assert.That(enabledRaster.Bounds, Is.EqualTo(requestedRegion));
        });
    }

    [Test]
    public void OrdinaryCpuBackend_PreservesExplicitProgramValidationFailure()
    {
        using var source = new CpuRenderTarget(6, 4);
        ShaderDescription invalid = ShaderDescription.WholeSource(
            "uniform shader src; half4 main(float2 p) { this is not valid SkSL; }",
            RenderBoundsContract.Identity);
        using var node = new ShaderNode(source, invalid);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
                FusionMode = FusionMode.Disabled,
            });

        Assert.That(
            renderer.Rasterize,
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.StartsWith("SkSL program validation failed:"));
    }

    [TestCase(ShaderDescriptionKind.WholeSource)]
    [TestCase(ShaderDescriptionKind.CurrentPixel)]
    public void CompatibilityShaderProgramCache_ColdMissThenWarmHit(
        ShaderDescriptionKind kind)
    {
        using var source = new CpuRenderTarget(6, 4);
        source.Value.Canvas.Clear(new SKColor(64, 128, 192, 160));
        ShaderDescription description;
        if (kind == ShaderDescriptionKind.WholeSource)
        {
            description = ShaderDescription.WholeSource(
                "uniform shader src; half4 main(float2 p) { return src.eval(p).bgra; }",
                RenderBoundsContract.Identity);
        }
        else
        {
            string currentPixelSource =
                $"/*{new string('界', 22_000)}*/\n"
                + "half4 apply(half4 color) { return color.bgra; }";
            description = ShaderDescription.CurrentPixel(currentPixelSource);
            SkslMergedProgram fallback = SkslSnippetMerger.MergeAndSplit(
                [new SkslSnippetStage(description)],
                SkslBackendBudgetResolver.Portable)[0];
            Assert.That(
                fallback.OverflowReasons,
                Does.Contain(SkslBackendLimit.SourceBytes),
                "the test must exercise the backend-overflow compatibility path");
        }

        using var node = new ShaderNode(source, description);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
                FusionMode = FusionMode.Disabled,
            });

        using RenderNodeRasterization cold = renderer.Rasterize();
        ProgramCacheStatistics coldStatistics = renderer.ProgramCacheStatistics;
        using RenderNodeRasterization warm = renderer.Rasterize();
        ProgramCacheStatistics warmStatistics = renderer.ProgramCacheStatistics;

        Assert.Multiple(() =>
        {
            Assert.That(SumAbsoluteChannels(cold.Bitmap!), Is.GreaterThan(1));
            Assert.That(SumAbsoluteChannels(warm.Bitmap!), Is.GreaterThan(1));
            Assert.That(coldStatistics.Creations, Is.EqualTo(1));
            Assert.That(coldStatistics.Misses, Is.EqualTo(1));
            Assert.That(coldStatistics.Hits, Is.Zero);
            Assert.That(warmStatistics.Creations, Is.EqualTo(1));
            Assert.That(warmStatistics.Misses, Is.EqualTo(1));
            Assert.That(warmStatistics.Hits, Is.EqualTo(1));
            Assert.That(renderer.LastExecutionStatistics.ProgramCacheHits, Is.EqualTo(1));
        });
    }

    private static double SumAbsoluteChannels(Bitmap bitmap)
    {
        double result = 0;
        foreach (ushort bits in bitmap.GetPixelSpan<ushort>())
            result += Math.Abs((float)BitConverter.UInt16BitsToHalf(bits));
        return result;
    }

    private static void AssertBlueRedSwap(Bitmap source, Bitmap actual)
    {
        (float sourceRed, float sourceGreen, float sourceBlue, float sourceAlpha) = ChannelsAt(source, 0, 0);
        (float actualRed, float actualGreen, float actualBlue, float actualAlpha) = ChannelsAt(actual, 0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(sourceRed, Is.Not.EqualTo(sourceBlue).Within(0.001f),
                "the source fixture must distinguish a skipped shader from a red/blue swap");
            Assert.That(actualRed, Is.EqualTo(sourceBlue).Within(0.002f));
            Assert.That(actualGreen, Is.EqualTo(sourceGreen).Within(0.002f));
            Assert.That(actualBlue, Is.EqualTo(sourceRed).Within(0.002f));
            Assert.That(actualAlpha, Is.EqualTo(sourceAlpha).Within(0.002f));
        });
    }

    private static (float Red, float Green, float Blue, float Alpha) ChannelsAt(
        Bitmap bitmap,
        int x,
        int y)
    {
        Span<ushort> row = bitmap.GetRow<ushort>(y);
        int offset = x * 4;
        return (
            (float)BitConverter.UInt16BitsToHalf(row[offset]),
            (float)BitConverter.UInt16BitsToHalf(row[offset + 1]),
            (float)BitConverter.UInt16BitsToHalf(row[offset + 2]),
            (float)BitConverter.UInt16BitsToHalf(row[offset + 3]));
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        Rect requestedRegion,
        FusionMode fusionMode)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                RequestedRegion = requestedRegion,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
                FusionMode = fusionMode,
            });

    private sealed class ShaderNode(RenderTarget source, ShaderDescription description) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<RenderTarget> resource = context.Borrow(source, "fallback-source", 1);
            RenderFragmentHandle input = context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    resource,
                    s_bounds,
                    EffectiveScale.At(1),
                    RenderHitTestContract.OutputBounds));
            context.Publish(context.Shader(input, description));
        }
    }

    private sealed class BoundShaderChainNode : RenderNode
    {
        private readonly RenderTarget _source;
        private readonly IReadOnlyList<ShaderDescription> _stages;

        public BoundShaderChainNode(RenderTarget source, ICollection<Rect> observedInputBounds)
        {
            _source = source;
            _stages =
            [
                CreateStage(0, observedInputBounds),
                CreateStage(1, observedInputBounds),
            ];
        }

        public override void Process(RenderNodeContext context)
        {
            RenderResource<RenderTarget> resource = context.Borrow(
                _source,
                "bound-fallback-source",
                1);
            RenderFragmentHandle current = context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    resource,
                    s_bounds,
                    EffectiveScale.At(1),
                    RenderHitTestContract.OutputBounds));
            foreach (ShaderDescription stage in _stages)
                current = context.Shader(current, stage);
            context.Publish(current);
        }

        private static ShaderDescription CreateStage(
            int stage,
            ICollection<Rect> observedInputBounds)
            => ShaderDescription.CurrentPixel(
                "uniform float gain; half4 apply(half4 color) { return color * gain; }",
                bindings => bindings.Uniform(
                    "gain",
                    1f,
                    (writer, value, execution) =>
                    {
                        observedInputBounds.Add(execution.InputBounds);
                        writer.Set(value);
                    },
                    structuralKey: (typeof(BoundShaderChainNode), stage),
                    runtimeIdentity: new RenderRuntimeIdentity(stage)));
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(PixelSize deviceSize)
            => new CpuRenderTarget(deviceSize.Width, deviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}

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

    private static double SumAbsoluteChannels(Bitmap bitmap)
    {
        double result = 0;
        foreach (ushort bits in bitmap.GetPixelSpan<ushort>())
            result += Math.Abs((float)BitConverter.UInt16BitsToHalf(bits));
        return result;
    }

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

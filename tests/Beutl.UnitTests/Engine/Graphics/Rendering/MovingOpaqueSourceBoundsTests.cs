using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class MovingOpaqueSourceBoundsTests
{
    private static readonly Rect s_domain = new(0, 0, 200, 200);
    private static readonly Size s_size = new(10, 10);

    [Test]
    public void TwoNodesOfOneTypeAtDifferentPlaces_CompileOnePlanAndPublishTheirOwnBounds()
    {
        using var root = new ContainerRenderNode();
        root.AddChild(new MovingSourceNode(new Point(0, 0)));
        using RenderNodeRenderer renderer = CreateRenderer(root);

        Rect first = RenderAndMeasure(renderer);
        long afterFirstNode = renderer.StructuralPlanCacheStatistics.Compilations;
        root.SetChild(0, new MovingSourceNode(new Point(100, 40)));
        Rect second = RenderAndMeasure(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new Rect(0, 0, 10, 10)));
            Assert.That(
                second,
                Is.EqualTo(new Rect(100, 40, 10, 10)),
                "a definition built inside Process publishes the rectangle its node is standing at");
            Assert.That(afterFirstNode, Is.EqualTo(1));
            Assert.That(
                renderer.StructuralPlanCacheStatistics.Compilations,
                Is.EqualTo(1),
                "where the source stands is request data, so a second node of the same type must re-run "
                + "the compiled plan rather than compile a second one");
            Assert.That(renderer.StructuralPlanCacheStatistics.Hits, Is.GreaterThan(0));
        });
    }

    private static Rect RenderAndMeasure(RenderNodeRenderer renderer)
    {
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Assert.That(rasterization.Bitmap, Is.Not.Null);
        return renderer.Measure().OutputBounds;
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode root)
        => new(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = s_domain,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    /// <summary>A source whose published rectangle is the place the node holds.</summary>
    private sealed class MovingSourceNode(Point origin) : RenderNode
    {
        public Point Origin { get; } = origin;

        public override void Process(RenderNodeContext context)
        {
            var bounds = new Rect(Origin, s_size);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                bounds,
                Draw,
                OpaqueRenderBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Vector);
            context.Publish(context.OpaqueSource(description));
        }

        private void Draw(OpaqueRenderSession session, Rect bounds)
        {
            using OpaqueRenderOutput output = session.CreateOutput(bounds);
            output.Canvas.Use(static canvas => canvas.Clear(Color.FromArgb(255, 255, 255, 255)));
            session.Publish(output);
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
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

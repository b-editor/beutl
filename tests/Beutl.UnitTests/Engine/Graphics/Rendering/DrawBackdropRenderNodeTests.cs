using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class DrawBackdropRenderNodeTests
{
    private static readonly Rect s_domain = new(0, 0, 120, 90);

    [Test]
    public void BuiltInBackdrop_RecordsOverAZeroAreaCanvasWithoutReportingAPhantomHit()
    {
        using var root = new ContainerRenderNode();
        using (var context = new GraphicsContext2D(root))
        {
            context.DrawBackdrop(context.Snapshot());
        }

        using RenderNodeRenderer renderer = CreateRenderer(root);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.QueryBounds, Is.EqualTo(Rect.Empty));
            Assert.That(renderer.HitTest(default), Is.False);
        });
    }

    [Test]
    public void RawBackdrop_RecordsOverAZeroAreaCanvasWithoutReportingAPhantomHit()
    {
        using var root = new ContainerRenderNode();
        using (var context = new GraphicsContext2D(root))
        {
            context.DrawBackdrop(new StubBackdrop());
        }

        using RenderNodeRenderer renderer = CreateRenderer(root);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.QueryBounds, Is.EqualTo(Rect.Empty));
            Assert.That(renderer.HitTest(default), Is.False);
        });
    }

    [Test]
    public void Backdrop_KeepsOutputBoundsHitTestingOverAPositiveAreaCanvas()
    {
        using var root = new ContainerRenderNode();
        using (var context = new GraphicsContext2D(root, s_domain.Size))
        {
            context.DrawBackdrop(new StubBackdrop());
        }

        using RenderNodeRenderer renderer = CreateRenderer(root);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.QueryBounds, Is.EqualTo(s_domain));
            Assert.That(renderer.HitTest(new Point(60, 45)), Is.True);
            Assert.That(renderer.HitTest(new Point(200, 45)), Is.False);
        });
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode root)
        => new(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_domain,
                    CacheOptions = RenderCacheOptions.Disabled,
                },
            });

    private sealed class StubBackdrop : IBackdrop
    {
        public void Draw(ImmediateCanvas canvas)
        {
        }
    }
}

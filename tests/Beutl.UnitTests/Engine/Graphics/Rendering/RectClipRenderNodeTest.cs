using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RectClipRenderNodeTest
{
    [Test]
    public void Update_ShouldReturnFalse_WhenAllPropertiesMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        var operation = ClipOperation.Intersect;
        var node = new RectClipRenderNode(rect, operation);

        Assert.That(node.Update(rect, operation), Is.False);
    }

    [Test]
    public void Update_ShouldReturnTrue_WhenPropertiesDoNotMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        var operation = ClipOperation.Intersect;
        var node = new RectClipRenderNode(rect, operation);

        Assert.That(node.Update(default, operation), Is.True);
    }

    [Test]
    public void Measure_WithoutChild_ShouldReportNoFragments()
    {
        using var node = new RectClipRenderNode(new Rect(0, 0, 100, 100), ClipOperation.Intersect);
        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.HasFragments, Is.False);
    }

    [Test]
    public void Measure_WithChild_ShouldReportScopedFragment()
    {
        using var node = new RectClipRenderNode(new Rect(0, 0, 100, 100), ClipOperation.Intersect);
        node.AddChild(new RectangleRenderNode(
            new Rect(10, 20, 30, 40),
            Brushes.Resource.White,
            null));
        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(10, 20, 30, 40)));
        });
    }

    [Test]
    public void Intersect_ClipsOutputBoundsAndHitTesting()
    {
        var clip = new Rect(20, 10, 30, 40);
        using var node = new RectClipRenderNode(clip, ClipOperation.Intersect);
        node.AddChild(new RectangleRenderNode(
            new Rect(0, 0, 100, 100),
            Brushes.Resource.White,
            null));
        using var renderer = CreateRenderer(node);

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(clip));
            Assert.That(measurement.QueryBounds, Is.EqualTo(clip));
            Assert.That(renderer.HitTest(new Point(25, 25)), Is.True);
            Assert.That(renderer.HitTest(new Point(10, 25)), Is.False);
        });
    }

    [Test]
    public void RuntimeClipChanges_ReuseTheStructuralPlan()
    {
        using var cache = new StructuralPlanCache();
        using var node = new RectClipRenderNode(
            new Rect(10, 10, 40, 40),
            ClipOperation.Intersect);
        node.AddChild(new RectangleRenderNode(
            new Rect(0, 0, 100, 100),
            Brushes.Resource.White,
            null));

        using (Compile(cache, node))
        {
        }

        node.Update(new Rect(20, 20, 30, 30), ClipOperation.Difference);
        using CompiledRenderRequest compiled = Compile(cache, node);

        Assert.Multiple(() =>
        {
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 100, 100)));
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(1));
            Assert.That(cache.Statistics.Hits, Is.EqualTo(1));
        });
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(node, new RenderNodeRendererOptions { UseRenderCache = false });

    private static CompiledRenderRequest Compile(StructuralPlanCache cache, RenderNode node)
    {
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            cachePolicy: RenderCacheOptions.Disabled));
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
            return new RenderRequestCompiler(cache).Compile(request, graph);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }
}

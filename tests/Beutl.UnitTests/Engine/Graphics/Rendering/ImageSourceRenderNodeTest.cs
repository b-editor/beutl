using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class ImageSourceRenderNodeTest
{
    private ImageSource? _imageSource;
    private ImageSource.Resource? _imageSourceResource;

    [SetUp]
    public void SetUp()
    {
        var uri = TestMediaHelper.CreateTestImageUri(100, 100, Colors.White);
        _imageSource = new ImageSource();
        _imageSource.ReadFrom(uri);
        _imageSourceResource = _imageSource.ToResource(CompositionContext.Default);
    }

    [TearDown]
    public void TearDown()
    {
        _imageSourceResource?.Dispose();
        _imageSourceResource = null;
        _imageSource = null;
    }

    public ImageSource.Resource GetTestImageSourceResource()
    {
        return _imageSourceResource!;
    }

    [Test]
    public void Update_ShouldReturnFalse_WhenAllPropertiesMatch()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        var fill = Brushes.Resource.White;
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 1;
        var penResource = pen.ToResource(CompositionContext.Default);
        var node = new ImageSourceRenderNode(source, fill, penResource);

        Assert.That(node.Update(source, fill, penResource), Is.False);
    }

    [Test]
    public void Update_ShouldReturnTrue_WhenPropertiesDoNotMatch()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        var fill = Brushes.Resource.White;
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 1;
        var penResource = pen.ToResource(CompositionContext.Default);
        var node = new ImageSourceRenderNode(source, fill, penResource);

        pen.Thickness.CurrentValue = 2;
        var updateOnly = false;
        penResource.Update(pen, CompositionContext.Default, ref updateOnly);

        Assert.That(node.Update(source, fill, penResource), Is.True);
    }

    [Test]
    public void Measure_WithoutInput_ShouldReportRecordedFragment()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        using var node = new ImageSourceRenderNode(source, null, null);
        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.HasFragments, Is.True);
    }

    [Test]
    public void Measure_WithInput_ShouldReportRecordedFragment()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        using var node = new InputFeedingNode(new ImageSourceRenderNode(source, null, null));
        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.HasFragments, Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideStroke()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 50;
        var penResource = pen.ToResource(CompositionContext.Default);
        using var node = new ImageSourceRenderNode(source, null, penResource);
        using var renderer = CreateRenderer(node);
        var point = new Point(-10, -10);

        Assert.That(renderer.HitTest(point), Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnFalse_WhenPointIsOutsideStroke()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 50;
        var penResource = pen.ToResource(CompositionContext.Default);
        using var node = new ImageSourceRenderNode(source, null, penResource);
        using var renderer = CreateRenderer(node);
        var point = new Point(60, 60);

        Assert.That(renderer.HitTest(point), Is.False);
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideFill()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        Brush.Resource fill = Brushes.Resource.White;
        using var node = new ImageSourceRenderNode(source, fill, null);
        using var renderer = CreateRenderer(node);
        var point = new Point(50, 50);

        Assert.That(renderer.HitTest(point), Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnFalse_WhenPointIsOutsideFill()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        Brush.Resource fill = Brushes.Resource.White;
        using var node = new ImageSourceRenderNode(source, fill, null);
        using var renderer = CreateRenderer(node);
        var point = new Point(150, 150);

        Assert.That(renderer.HitTest(point), Is.False);
    }

    // A decoded image reports concrete At(1) density, not Unbounded.
    [Test]
    public void Measure_ReportsConcreteNativeDensity_NotUnbounded()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        using var node = new ImageSourceRenderNode(source, Brushes.Resource.White, null);
        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False,
            "an image source must report a concrete density, not the vector Unbounded sentinel");
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(1f),
            "an image drawn at its native 1:1 size has supply density 1");
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(node, new RenderNodeRendererOptions { UseRenderCache = false });

    private sealed class InputFeedingNode(RenderNode child) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle input = context.OpaqueSource(
                OpaqueRenderDescription.Create(
                    static _ => throw new AssertionException("Metadata recording must not execute opaque callbacks."),
                    RenderOperationBoundsContract.Source(new Rect(0, 0, 1, 1)),
                    RenderHitTestContract.None,
                    RenderValueCardinality.Single,
                    RenderScaleContract.Vector,
                    structuralKey: typeof(InputFeedingNode)));
            context.PublishRange(context.RecordNode(child, [input]));
        }

        protected override void OnDispose(bool disposing)
        {
            child.Dispose();
            base.OnDispose(disposing);
        }
    }
}

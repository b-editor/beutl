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
        _imageSourceResource = _imageSource.ToResource(RenderContext.Default);
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
        var penResource = pen.ToResource(RenderContext.Default);
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
        var penResource = pen.ToResource(RenderContext.Default);
        var node = new ImageSourceRenderNode(source, fill, penResource);

        pen.Thickness.CurrentValue = 2;
        var updateOnly = false;
        penResource.Update(pen, RenderContext.Default, ref updateOnly);

        Assert.That(node.Update(source, fill, penResource), Is.True);
    }

    [Test]
    public void Process_WithoutInput_ShouldReturnEmptyRenderNodeOperation()
    {
        var context = new RenderNodeContext([]);

        ImageSource.Resource source = GetTestImageSourceResource();
        var node = new ImageSourceRenderNode(source, null, null);
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Empty);
    }

    [Test]
    public void Process_WithInput_ShouldReturnExpectedRenderNodeOperation()
    {
        var context = new RenderNodeContext([
            RenderNodeOperation.CreateLambda(default, _ => { })
        ]);

        ImageSource.Resource source = GetTestImageSourceResource();
        var node = new ImageSourceRenderNode(source, null, null);
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Empty);
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideStroke()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 50;
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new ImageSourceRenderNode(source, null, penResource);
        var operations = node.Process(context);
        var point = new Point(-10, -10);

        Assert.That(operations[0].HitTest(point), Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnFalse_WhenPointIsOutsideStroke()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 50;
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new ImageSourceRenderNode(source, null, penResource);
        var operations = node.Process(context);
        var point = new Point(60, 60);

        Assert.That(operations[0].HitTest(point), Is.False);
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideFill()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        Brush.Resource fill = Brushes.Resource.White;
        var context = new RenderNodeContext([]);

        var node = new ImageSourceRenderNode(source, fill, null);
        var operations = node.Process(context);
        var point = new Point(50, 50);

        Assert.That(operations[0].HitTest(point), Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnFalse_WhenPointIsOutsideFill()
    {
        ImageSource.Resource source = GetTestImageSourceResource();
        Brush.Resource fill = Brushes.Resource.White;
        var context = new RenderNodeContext([]);

        var node = new ImageSourceRenderNode(source, fill, null);
        var operations = node.Process(context);
        var point = new Point(150, 150);

        Assert.That(operations[0].HitTest(point), Is.False);
    }
}

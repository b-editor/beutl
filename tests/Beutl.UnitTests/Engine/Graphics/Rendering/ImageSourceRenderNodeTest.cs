using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class ImageSourceRenderNodeTest
{
    public IImageSource CreateMockImageSource()
    {
        var source = new Mock<IImageSource>();
        source.Setup(x => x.FrameSize).Returns(new PixelSize(100, 100));
        source.Setup(x => x.Clone()).Returns(() => source.Object);

        return source.Object;
    }

    [Test]
    public void Update_ShouldReturnFalse_WhenAllPropertiesMatch()
    {
        IImageSource source = CreateMockImageSource();
        var fill = Brushes.Resource.White;
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue= 1;
        var penResource = pen.ToResource(RenderContext.Default);
        var node = new ImageSourceRenderNode(source, fill, penResource);

        Assert.That(node.Update(source, fill, penResource), Is.False);
    }

    [Test]
    public void Update_ShouldReturnTrue_WhenPropertiesDoNotMatch()
    {
        IImageSource source = CreateMockImageSource();
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

        IImageSource source = CreateMockImageSource();
        var node = new ImageSourceRenderNode(source, null, null);
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Empty);
    }

    [Test]
    public void Process_WithInput_ShouldReturnExpectedRenderNodeOperation()
    {
        var context = new RenderNodeContext([
            RenderNodeOperation.CreateLambda(default, _ => {  })
        ]);

        IImageSource source = CreateMockImageSource();
        var node = new ImageSourceRenderNode(source, null, null);
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Empty);
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideStroke()
    {
        IImageSource source = CreateMockImageSource();
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
        IImageSource source = CreateMockImageSource();
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
        IImageSource source = CreateMockImageSource();
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
        IImageSource source = CreateMockImageSource();
        Brush.Resource fill = Brushes.Resource.White;
        var context = new RenderNodeContext([]);

        var node = new ImageSourceRenderNode(source, fill, null);
        var operations = node.Process(context);
        var point = new Point(150, 150);

        Assert.That(operations[0].HitTest(point), Is.False);
    }
}

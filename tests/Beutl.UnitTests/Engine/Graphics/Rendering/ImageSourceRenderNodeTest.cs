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
    public void Equals_ShouldReturnTrue_WhenAllPropertiesMatch()
    {
        IImageSource source = CreateMockImageSource();
        IBrush fill = Brushes.White;
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 1 };
        var node = new ImageSourceRenderNode(source, fill, pen);

        Assert.That(node.Equals(source, fill, pen), Is.True);
    }

    [Test]
    public void Equals_ShouldReturnFalse_WhenPropertiesDoNotMatch()
    {
        IImageSource source = CreateMockImageSource();
        IBrush fill = Brushes.White;
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 1 };
        var node = new ImageSourceRenderNode(source, fill, pen);

        Assert.That(node.Equals(Mock.Of<IImageSource>(), fill, pen), Is.False);
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
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 50 };
        var context = new RenderNodeContext([]);

        var node = new ImageSourceRenderNode(source, null, pen);
        var operations = node.Process(context);
        var point = new Point(-10, -10);

        Assert.That(operations[0].HitTest(point), Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnFalse_WhenPointIsOutsideStroke()
    {
        IImageSource source = CreateMockImageSource();
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 50 };
        var context = new RenderNodeContext([]);

        var node = new ImageSourceRenderNode(source, null, pen);
        var operations = node.Process(context);
        var point = new Point(60, 60);

        Assert.That(operations[0].HitTest(point), Is.False);
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideFill()
    {
        IImageSource source = CreateMockImageSource();
        IBrush fill = Brushes.White;
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
        IBrush fill = Brushes.White;
        var context = new RenderNodeContext([]);

        var node = new ImageSourceRenderNode(source, fill, null);
        var operations = node.Process(context);
        var point = new Point(150, 150);

        Assert.That(operations[0].HitTest(point), Is.False);
    }
}

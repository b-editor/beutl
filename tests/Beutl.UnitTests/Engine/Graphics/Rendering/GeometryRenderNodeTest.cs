using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class GeometryRenderNodeTest
{
    [Test]
    public void Update_ShouldReturnFalse_WhenAllPropertiesMatch()
    {
        var geometry = new EllipseGeometry { Width = { CurrentValue = 100 }, Height = { CurrentValue = 100 } };
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var geometryResource = geometry.ToResource(RenderContext.Default);
        var fillResource = fill.ToResource(RenderContext.Default);
        var penResource = pen.ToResource(RenderContext.Default);

        var node = new GeometryRenderNode(geometryResource, fillResource, penResource);

        Assert.That(node.Update(geometryResource, fillResource, penResource), Is.False);
    }

    [Test]
    public void Update_ShouldReturnTrue_WhenPropertiesDoNotMatch()
    {
        var geometry1 = new EllipseGeometry { Width = { CurrentValue = 100 }, Height = { CurrentValue = 100 } };
        var geometry2 = new EllipseGeometry { Width = { CurrentValue = 100 }, Height = { CurrentValue = 100 } };
        Brush fill1 = new SolidColorBrush(Colors.Red);
        Brush fill2 = new SolidColorBrush(Colors.Blue);
        Pen pen1 = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        Pen pen2 = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 2 } };
        var geometryResource1 = geometry1.ToResource(RenderContext.Default);
        var geometryResource2 = geometry2.ToResource(RenderContext.Default);
        var fillResource1 = fill1.ToResource(RenderContext.Default);
        var fillResource2 = fill2.ToResource(RenderContext.Default);
        var penResource1 = pen1.ToResource(RenderContext.Default);
        var penResource2 = pen2.ToResource(RenderContext.Default);

        var node = new GeometryRenderNode(geometryResource1, fillResource1, penResource1);

        Assert.That(node.Update(geometryResource2, fillResource1, penResource1), Is.True);
        Assert.That(node.Update(geometryResource1, fillResource2, penResource1), Is.True);
        Assert.That(node.Update(geometryResource1, fillResource1, penResource2), Is.True);
    }

    [Test]
    public void Process_ShouldReturnCorrectRenderNodeOperation()
    {
        var geometry = new EllipseGeometry { Width = { CurrentValue = 100 }, Height = { CurrentValue = 100 } };
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var geometryResource = geometry.ToResource(RenderContext.Default);
        var fillResource = fill.ToResource(RenderContext.Default);
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new GeometryRenderNode(geometryResource, fillResource, penResource);
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Null);
        Assert.That(operations.Length, Is.EqualTo(1));
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideGeometry()
    {
        var geometry = new EllipseGeometry { Width = { CurrentValue = 100 }, Height = { CurrentValue = 100 } };
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var geometryResource = geometry.ToResource(RenderContext.Default);
        var fillResource = fill.ToResource(RenderContext.Default);
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new GeometryRenderNode(geometryResource, fillResource, penResource);
        var operations = node.Process(context);
        var point = new Point(50, 50);

        Assert.That(operations[0].HitTest(point), Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnFalse_WhenPointIsOutsideGeometry()
    {
        var geometry = new EllipseGeometry { Width = { CurrentValue = 100 }, Height = { CurrentValue = 100 } };
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var geometryResource = geometry.ToResource(RenderContext.Default);
        var fillResource = fill.ToResource(RenderContext.Default);
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new GeometryRenderNode(geometryResource, fillResource, penResource);
        var operations = node.Process(context);
        var point = new Point(150, 150);

        Assert.That(operations[0].HitTest(point), Is.False);
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideGeometryStroke()
    {
        var geometry = new EllipseGeometry { Width = { CurrentValue = 100 }, Height = { CurrentValue = 100 } };
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 50 } };
        var geometryResource = geometry.ToResource(RenderContext.Default);
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new GeometryRenderNode(geometryResource, null, penResource);
        var operations = node.Process(context);
        var point = new Point(0, 50);

        Assert.That(operations[0].HitTest(point), Is.True);
    }
}

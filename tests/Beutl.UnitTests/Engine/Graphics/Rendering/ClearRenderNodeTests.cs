using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Moq;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class ClearRenderNodeTest
{
    [Test]
    public void Constructor_ShouldInitializeColor()
    {
        // Arrange
        var color = new Color(255, 0, 0, 255);

        // Act
        var node = new ClearRenderNode(color);

        // Assert
        Assert.That(node.Color, Is.EqualTo(color));
    }

    [Test]
    public void Equals_ShouldReturnTrueForSameColor()
    {
        // Arrange
        var color = new Color(255, 0, 0, 255);
        var node = new ClearRenderNode(color);

        // Act
        var result = node.Equals(color);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void Equals_ShouldReturnFalseForDifferentColor()
    {
        // Arrange
        var color1 = new Color(255, 0, 0, 255);
        var color2 = new Color(0, 255, 0, 255);
        var node = new ClearRenderNode(color1);

        // Act
        var result = node.Equals(color2);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void Process_ShouldReturnRenderNodeOperation()
    {
        // Arrange
        var color = new Color(255, 0, 0, 255);
        var node = new ClearRenderNode(color);
        var context = new RenderNodeContext([]);
        using var renderTarget = RenderTarget.CreateNull(100, 100);
        using var canvas = new ImmediateCanvas(renderTarget);

        // Act
        var operations = node.Process(context);

        // Assert
        Assert.That(operations, Is.Not.Null);
        Assert.That(operations.Length, Is.EqualTo(1));
        Assert.That(operations[0], Is.InstanceOf<RenderNodeOperation>());
        Assert.That(operations[0].Bounds, Is.EqualTo(Rect.Empty));
        Assert.That(() => operations[0].Render(canvas), Throws.Nothing);

        operations[0].Dispose();
    }
}

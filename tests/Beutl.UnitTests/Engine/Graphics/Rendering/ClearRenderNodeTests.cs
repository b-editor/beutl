using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

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
    public void Update_ShouldReturnFalseForSameColor()
    {
        // Arrange
        var color = new Color(255, 0, 0, 255);
        var node = new ClearRenderNode(color);

        // Act
        var result = node.Update(color);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void Update_ShouldReturnTrueForDifferentColor()
    {
        // Arrange
        var color1 = new Color(255, 0, 0, 255);
        var color2 = new Color(0, 255, 0, 255);
        var node = new ClearRenderNode(color1);

        // Act
        var result = node.Update(color2);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void Render_ShouldRecordAndExecuteTargetCommand()
    {
        // Arrange
        var color = new Color(255, 0, 0, 255);
        var node = new ClearRenderNode(color);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = new Rect(0, 0, 100, 100),
                UseRenderCache = false,
            });
        using var renderTarget = RenderTarget.CreateNull(100, 100);
        using var canvas = new ImmediateCanvas(renderTarget);

        // Act
        RenderNodeMeasurement measurement = renderer.Measure();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasContributingValues, Is.False);
            Assert.That(measurement.HasTargetEffects, Is.True);
            Assert.That(measurement.QueryBounds, Is.EqualTo(Rect.Empty));
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 100, 100)));
            Assert.That(() => renderer.Render(canvas), Throws.Nothing);
        });
    }
}

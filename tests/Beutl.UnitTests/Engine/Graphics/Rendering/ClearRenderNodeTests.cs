using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class ClearRenderNodeTest
{
    [Test]
    public void Constructor_ShouldInitializeColor()
    {
        var color = new Color(255, 0, 0, 255);

        var node = new ClearRenderNode(color);

        Assert.That(node.Color, Is.EqualTo(color));
    }

    [Test]
    public void Update_ShouldReturnFalseForSameColor()
    {
        var color = new Color(255, 0, 0, 255);
        var node = new ClearRenderNode(color);

        var result = node.Update(color);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Update_ShouldReturnTrueForDifferentColor()
    {
        var color1 = new Color(255, 0, 0, 255);
        var color2 = new Color(0, 255, 0, 255);
        var node = new ClearRenderNode(color1);

        var result = node.Update(color2);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Render_ShouldRecordAndExecuteTargetCommand()
    {
        var color = new Color(255, 0, 0, 255);
        var node = new ClearRenderNode(color);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = new Rect(0, 0, 100, 100),
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        using var renderTarget = RenderTarget.CreateNull(100, 100);
        using var canvas = new ImmediateCanvas(renderTarget, RenderIntent.Preview);

        RenderNodeMeasurement measurement = renderer.Measure();

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

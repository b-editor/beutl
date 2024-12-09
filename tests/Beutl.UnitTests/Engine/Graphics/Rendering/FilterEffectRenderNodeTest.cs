using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Moq;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class FilterEffectRenderNodeTest
{
    private static IImmediateCanvasFactory CreateCanvasFactory()
    {
        var mock = new Mock<IImmediateCanvasFactory>();
        mock.Setup(m => m.CreateCanvas(It.IsNotNull<SKSurface>(), It.IsAny<bool>()))
            .Returns(new Func<SKSurface, bool, ImmediateCanvas>((s, r) => new ImmediateCanvas(s, r)));

        mock.Setup(m => m.CreateRenderTarget(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new Func<int, int, SKSurface>((w, h) => SKSurface.CreateNull(w, h)));

        mock.Setup(m => m.GetCacheContext())
            .Returns(() => null);

        return mock.Object;
    }

    private static RenderNodeContext CreateRenderNodeContext()
    {
        return new RenderNodeContext(CreateCanvasFactory(), [
            RenderNodeOperation.CreateLambda(
                new Rect(0, 0, 100, 100),
                canvas => canvas.DrawEllipse(new Rect(0, 0, 100, 100), Brushes.White, null),
                point => false
            )
        ]);
    }

    [Test]
    public void Process_ShouldReturnRenderNodeOperations()
    {
        var effect = new Blur();
        var node = new FilterEffectRenderNode(effect);
        var context = CreateRenderNodeContext();
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Empty);
    }

    [Test]
    public void Process_ShouldApplyFilterEffect()
    {
        var effect = new Blur() { Sigma = new(10, 10) };
        var node = new FilterEffectRenderNode(effect);
        var context = CreateRenderNodeContext();
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Empty);
        Assert.That(operations[0].Bounds.X, Is.LessThan(0));
        Assert.That(operations[0].Bounds.Y, Is.LessThan(0));
        Assert.That(operations[0].Bounds.Width, Is.GreaterThan(100));
        Assert.That(operations[0].Bounds.Height, Is.GreaterThan(100));
    }

    [Test]
    public void Equals_ShouldReturnTrueForSameFilterEffect()
    {
        var effect = new Blur();
        var node = new FilterEffectRenderNode(effect);

        var result = node.Equals(effect);

        Assert.That(result, Is.True);
    }

    // Effectのプロパティを変更するとEqualsがfalseを返す
    [Test]
    public void Equals_ShouldReturnFalseForDifferentFilterEffectProperty()
    {
        var effect = new Blur();
        var node = new FilterEffectRenderNode(effect);
        effect.Sigma = new(10, 10);

        var result = node.Equals(effect);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Equals_ShouldReturnFalseForDifferentFilterEffect()
    {
        var effect1 = new Blur();
        var effect2 = new DropShadow();
        var node = new FilterEffectRenderNode(effect1);

        var result = node.Equals(effect2);

        Assert.That(result, Is.False);
    }
}

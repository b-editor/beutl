using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class FilterEffectRenderNodeTest
{
    private static RenderNodeContext CreateRenderNodeContext()
    {
        return new RenderNodeContext([
            RenderNodeOperation.CreateLambda(
                new Rect(0, 0, 100, 100),
                canvas => canvas.DrawEllipse(new Rect(0, 0, 100, 100), Brushes.Resource.White, null),
                point => false
            )
        ]);
    }

    [Test]
    public void Process_ShouldReturnRenderNodeOperations()
    {
        var effect = new Blur();
        var resource = effect.ToResource(RenderContext.Default);
        var node = new FilterEffectRenderNode(resource);
        var context = CreateRenderNodeContext();
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Empty);
    }

    [Test]
    public void Process_ShouldApplyFilterEffect()
    {
        var effect = new Blur() { Sigma = { CurrentValue = new(10, 10) } };
        var resource = effect.ToResource(RenderContext.Default);
        var node = new FilterEffectRenderNode(resource);
        var context = CreateRenderNodeContext();
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Empty);
        Assert.That(operations[0].Bounds.X, Is.LessThan(0));
        Assert.That(operations[0].Bounds.Y, Is.LessThan(0));
        Assert.That(operations[0].Bounds.Width, Is.GreaterThan(100));
        Assert.That(operations[0].Bounds.Height, Is.GreaterThan(100));
    }

    [Test]
    public void Update_ShouldReturnFalseForSameFilterEffect()
    {
        var effect = new Blur();
        var resource = effect.ToResource(RenderContext.Default);
        var node = new FilterEffectRenderNode(resource);

        var result = node.Update(resource);

        Assert.That(result, Is.False);
    }

    // Effectのプロパティを変更するとUpdateがTrueを返す
    [Test]
    public void Update_ShouldReturnTrueForDifferentFilterEffectProperty()
    {
        var effect = new Blur();
        var resource = effect.ToResource(RenderContext.Default);
        var node = new FilterEffectRenderNode(resource);
        effect.Sigma.CurrentValue = new(10, 10);
        var updateOnly = false;
        resource.Update(effect, RenderContext.Default, ref updateOnly);

        var result = node.Update(resource);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Update_ShouldReturnTrueForDifferentFilterEffect()
    {
        var effect1 = new Blur();
        var effect2 = new DropShadow();
        var effectResource1 = effect1.ToResource(RenderContext.Default);
        var effectResource2 = effect2.ToResource(RenderContext.Default);
        var node = new FilterEffectRenderNode(effectResource1);

        var result = node.Update(effectResource2);

        Assert.That(result, Is.True);
    }
}

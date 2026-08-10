using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class DetachedResourceTests
{
    [Test]
    public void ToBrushResource_ProducesADetachedResource()
    {
        SolidColorBrush.Resource resource = Colors.Red.ToBrushResource();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsAttached, Is.False);
            Assert.That(resource.GetOriginal(), Is.Null);
        });
    }

    [Test]
    public void RequireOriginal_OnADetachedResource_Throws()
    {
        SolidColorBrush.Resource resource = Colors.Red.ToBrushResource();

        Assert.That(resource.RequireOriginal, Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public void ToResource_ProducesAnAttachedResource()
    {
        var brush = new SolidColorBrush(Colors.Red);
        using SolidColorBrush.Resource resource = brush.ToResource(CompositionContext.Default);

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsAttached, Is.True);
            Assert.That(resource.GetOriginal(), Is.SameAs(brush));
            Assert.That(resource.RequireOriginal(), Is.SameAs(brush));
        });
    }

    [Test]
    public void GetOriginal_IsTypedToTheDeclaringEngineObject()
    {
        var brush = new SolidColorBrush(Colors.Red);
        using SolidColorBrush.Resource resource = brush.ToResource(CompositionContext.Default);

        SolidColorBrush? original = resource.GetOriginal();

        Assert.That(original, Is.SameAs(brush));
    }
}

using Beutl.AgentToolkit.Common;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Common;

public class ReferencePropertiesTests
{
    [TestCase(typeof(SceneDrawable), "ReferencedScene", typeof(Scene))]
    [TestCase(typeof(SceneSound), "ReferencedScene", typeof(Scene))]
    [TestCase(typeof(DrawablePresenter), "Target", typeof(Drawable))]
    [TestCase(typeof(DrawableTimeController), "Target", typeof(Drawable))]
    [TestCase(typeof(FilterEffectPresenter), "Target", typeof(FilterEffect))]
    [TestCase(typeof(TransformPresenter), "Target", typeof(Transform))]
    [TestCase(typeof(BrushPresenter), "Target", typeof(Brush))]
    public void ForOwner_ReadsTheDeclaredReference(Type ownerType, string name, Type referencedType)
    {
        IReadOnlyList<ReferencePropertyDescriptor> descriptors = ReferenceProperties.ForOwner(ownerType);

        Assert.That(descriptors, Is.EqualTo(new[] { new ReferencePropertyDescriptor(name, referencedType) }));
    }

    [Test]
    public void ForOwner_ReturnsEmptyForTypeWithoutReference()
    {
        Assert.That(ReferenceProperties.ForOwner(typeof(RectShape)), Is.Empty);
    }
}

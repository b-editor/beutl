using Beutl.AgentToolkit.Common;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Common;

public class ReferencePropertiesTests
{
    [TestCase(typeof(SceneDrawable))]
    [TestCase(typeof(SceneSound))]
    public void ForOwner_DetectsSceneReferenceByProjectItemValueType(Type ownerType)
    {
        IReadOnlyList<ReferencePropertyDescriptor> descriptors = ReferenceProperties.ForOwner(ownerType);

        Assert.That(descriptors.Count, Is.EqualTo(1));
        Assert.That(descriptors[0].Name, Is.EqualTo("ReferencedScene"));
        Assert.That(descriptors[0].ReferencedType, Is.EqualTo(typeof(Scene)));
    }

    [Test]
    public void ForOwner_ReturnsEmptyForTypeWithoutProjectItemProperty()
    {
        Assert.That(ReferenceProperties.ForOwner(typeof(RectShape)), Is.Empty);
    }
}

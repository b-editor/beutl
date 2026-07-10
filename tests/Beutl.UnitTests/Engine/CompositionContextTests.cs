using Beutl.Composition;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class CompositionContextTests
{
    [Test]
    public void Default_ReturnsFreshInstance_SoMutationDoesNotLeakGlobally()
    {
        CompositionContext first = CompositionContext.Default;
        CompositionContext second = CompositionContext.Default;

        first.PreferProxy = true;
        first.DisableResourceShare = true;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.SameAs(second), "Default must not be a shared mutable singleton.");
            Assert.That(second.PreferProxy, Is.False);
            Assert.That(CompositionContext.Default.DisableResourceShare, Is.False);
        });
    }
}

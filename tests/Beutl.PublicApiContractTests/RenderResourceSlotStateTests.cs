using System.Reflection;
using Beutl.Graphics.Rendering;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderResourceSlotStateTests
{
    [Test]
    public void TheTypedResourceSlotIsSealedAndDeclaresNoInstanceField()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                typeof(RenderResourceSlot<>).IsSealed,
                Is.True,
                "unsealed, a subclass could add the state the fields below are checked for");

            for (Type? type = typeof(RenderResourceSlot<>);
                type is not null && type != typeof(object);
                type = type.BaseType)
            {
                Assert.That(
                    type.GetFields(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly),
                    Is.Empty,
                    $"'{type}' carries instance state BESG004 accepts a slot for not having");
            }
        });
    }
}

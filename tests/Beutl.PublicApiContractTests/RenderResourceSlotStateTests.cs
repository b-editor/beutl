using System.Reflection;
using Beutl.Graphics.Rendering;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Pins that a resource slot carries no instance state.
/// </summary>
/// <remarks>
/// BESG004 refuses a static readonly field whose type is a class it was not shown the state of, because a
/// compilation imports a metadata class down to its public and protected members. Every author outside
/// Beutl.Engine reaches <see cref="RenderResourceSlot{T}"/> that way, and it is the type that rule's own
/// message sends them to, so the analyzer accepts it by name rather than by walking fields it cannot read.
/// That claim is only as good as the type staying an address and nothing else, which is what this pins:
/// give a slot an instance field and the analyzer would be clearing state it never looked at. The abstract
/// base is not covered because the analyzer does not clear it - the engine derives a stateful slot from it.
/// </remarks>
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

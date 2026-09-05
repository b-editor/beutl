using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class GeometrySessionTests
{


    private static void RenderNothing(GeometrySession session, (string Kind, int Value) state)
    {
    }

    [Test]
    public void Description_StructuralIdentityUsesFullValueEquality()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<object> firstObject = registry.RegisterBorrowed(new object());
        RenderResource<string> firstString = registry.RegisterBorrowed(new string('a', 1));
        RenderResource<object> secondObject = registry.RegisterBorrowed(new object());
        RenderResource<string> secondString = registry.RegisterBorrowed(new string('b', 1));

        GeometryDescription first = CreateDescription(
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            requiresReadback: true,
            resources: [GeometrySessionSlots.Object.Bind(firstObject), GeometrySessionSlots.Text.Bind(firstString)]);
        GeometryDescription equal = CreateDescription(
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            requiresReadback: true,
            resources: [GeometrySessionSlots.Object.Bind(secondObject), GeometrySessionSlots.Text.Bind(secondString)]);
        GeometryDescription differentBounds = CreateDescription(
            RenderBoundsContract.FullInput,
            RenderHitTestContract.AnyInput,
            requiresReadback: true,
            resources: [GeometrySessionSlots.Object.Bind(secondObject), GeometrySessionSlots.Text.Bind(secondString)]);
        GeometryDescription differentHitTest = CreateDescription(
            RenderBoundsContract.Identity,
            RenderHitTestContract.None,
            requiresReadback: true,
            resources: [GeometrySessionSlots.Object.Bind(secondObject), GeometrySessionSlots.Text.Bind(secondString)]);
        GeometryDescription differentReadback = CreateDescription(
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            requiresReadback: false,
            resources: [GeometrySessionSlots.Object.Bind(secondObject), GeometrySessionSlots.Text.Bind(secondString)]);
        GeometryDescription differentResourceOrder = CreateDescription(
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            requiresReadback: true,
            resources: [GeometrySessionSlots.Text.Bind(secondString), GeometrySessionSlots.Object.Bind(secondObject)]);

        Assert.Multiple(() =>
        {
            Assert.That(first.StructuralIdentity, Is.EqualTo(equal.StructuralIdentity));
            Assert.That(
                first.StructuralIdentity.GetHashCode(),
                Is.EqualTo(equal.StructuralIdentity.GetHashCode()));
            Assert.That(first.StructuralIdentity, Is.Not.EqualTo(differentBounds.StructuralIdentity));
            Assert.That(first.StructuralIdentity, Is.Not.EqualTo(differentHitTest.StructuralIdentity));
            Assert.That(first.StructuralIdentity, Is.Not.EqualTo(differentReadback.StructuralIdentity));
            Assert.That(first.StructuralIdentity, Is.Not.EqualTo(differentResourceOrder.StructuralIdentity));
        });

        static GeometryDescription CreateDescription(
            RenderBoundsContract bounds,
            RenderHitTestContract hitTest,
            bool requiresReadback,
            IReadOnlyList<RenderResourceBinding> resources)
        {
            return GeometryDescription.CreateRequestLocal(
                static _ => { },
                bounds,
                hitTest,
                requiresReadback: requiresReadback,
                resources: resources);
        }
    }
}

internal static class GeometrySessionSlots
{
    internal static readonly RenderResourceSlot<object> Object = new();
    internal static readonly RenderResourceSlot<string> Text = new();
}

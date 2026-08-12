using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class GeometrySessionTests
{


    [Test]
    public void Create_RejectsACapturingCallbackAndNamesTheStateParameter()
    {
        var color = Colors.Red;
        ArgumentException? rejection = Assert.Throws<ArgumentException>(
            () => GeometryDescription.Create(
                "under-specified",
                (session, _) => session.Canvas.Use(canvas => canvas.Clear(color)),
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput));

        Assert.Multiple(() =>
        {
            Assert.That(rejection!.ParamName, Is.EqualTo("render"));
            Assert.That(rejection.Message, Does.Contain("state"));
        });
    }

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
            IEnumerable<RenderResourceBinding> resources)
        {
            return GeometryDescription.CreateRequestLocal(
                static _ => { },
                bounds,
                hitTest,
                requiresReadback: requiresReadback,
                resources: resources);
        }
    }


    [Test]
    public void Session_AllowsOnlyContainedShrinkAndDiscardWins()
    {
        Rect allocated = new(10, 20, 30, 40);
        GeometrySession session = CreateSession(allocated, out RenderExecutionSessionToken token, out RenderTarget target);
        try
        {
            var shrink = new Rect(12, 23, 8, 9);
            session.SetOutputBounds(shrink);
            Assert.That(session.OutputBounds, Is.EqualTo(shrink));
            Assert.That(
                () => session.SetOutputBounds(new Rect(0, 0, 100, 100)),
                Throws.TypeOf<ArgumentException>());

            session.DiscardOutput();
            session.SetOutputBounds(new Rect(13, 24, 1, 1));
            Assert.Multiple(() =>
            {
                Assert.That(session.IsOutputDiscarded, Is.True);
                Assert.That(session.OutputBounds, Is.EqualTo(new Rect(13, 24, 1, 1)));
            });
        }
        finally
        {
            token.Complete();
            target.Dispose();
        }
    }

    private static GeometrySession CreateSession(
        Rect bounds,
        out RenderExecutionSessionToken token,
        out RenderTarget target)
    {
        token = new RenderExecutionSessionToken();
        var input = new RenderExecutionInput(
            token,
            bounds,
            EffectiveScale.At(1),
            static (_, _) => { },
            static (_, _) => { },
            createShader: null,
            createSnapshot: null,
            readbackDeclared: false);
        PixelRect deviceBounds = PixelRect.FromRect(bounds, 1);
        RenderTarget outputTarget = RenderTarget.CreateNull(deviceBounds.Width, deviceBounds.Height);
        target = outputTarget;
        var canvas = new RenderCallbackCanvas(
            token,
            density: 1,
            bounds,
            () => new ImmediateCanvas(outputTarget, 1, float.PositiveInfinity, bounds.Size),
            CallbackCanvasCapability.Draw);
        return new GeometrySession(
            token,
            input,
            bounds,
            bounds,
            deviceBounds,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: float.PositiveInfinity,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            canvas,
            []);
    }
}

internal static class GeometrySessionSlots
{
    internal static readonly RenderResourceSlot<object> Geometry = new();
    internal static readonly RenderResourceSlot<object> Object = new();
    internal static readonly RenderResourceSlot<string> Text = new();
}

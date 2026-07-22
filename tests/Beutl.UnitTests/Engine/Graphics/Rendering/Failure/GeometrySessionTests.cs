using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class GeometrySessionTests
{
    [Test]
    public void Description_RequiresBoundsHitTestAndStableIdentities()
    {
        using var registry = new RenderRequestResourceRegistry();
        var raw = new object();
        RenderResource<object> resource = registry.RegisterBorrowed(raw, "geometry-resource", 3);
        Action<GeometrySession> render = static _ => { };
        GeometryDescription description = GeometryDescription.Create(
            render,
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            structuralKey: "geometry",
            runtimeIdentity: new RenderRuntimeIdentity(("pixels", 4)),
            requiresReadback: true,
            resources: [resource]);

        Assert.Multiple(() =>
        {
            Assert.That(description.Bounds, Is.EqualTo(RenderBoundsContract.Identity));
            Assert.That(description.HitTest, Is.EqualTo(RenderHitTestContract.AnyInput));
            Assert.That(description.StructuralKey, Is.EqualTo("geometry"));
            Assert.That(description.RuntimeIdentity, Is.EqualTo(new RenderRuntimeIdentity(("pixels", 4))));
            Assert.That(description.RequiresReadback, Is.True);
            Assert.That(description.Resources, Is.EqualTo(new[] { resource }));
            Assert.That(description.Render, Is.SameAs(render));
            Assert.That(
                () => GeometryDescription.Create(null!, RenderBoundsContract.Identity, RenderHitTestContract.None),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => GeometryDescription.Create(render, default, RenderHitTestContract.None),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => GeometryDescription.Create(render, RenderBoundsContract.Identity, default),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => GeometryDescription.Create(
                    render,
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.None,
                    runtimeIdentity: default(RenderRuntimeIdentity)),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void Description_StructuralIdentityUsesFullValueEquality()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<object> firstObject = registry.RegisterBorrowed(new object(), "first-object", 1);
        RenderResource<string> firstString = registry.RegisterBorrowed(new string('a', 1), "first-string", 1);
        RenderResource<object> secondObject = registry.RegisterBorrowed(new object(), "second-object", 1);
        RenderResource<string> secondString = registry.RegisterBorrowed(new string('b', 1), "second-string", 1);

        GeometryDescription first = CreateDescription(
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            requiresReadback: true,
            resources: [firstObject, firstString]);
        GeometryDescription equal = CreateDescription(
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            requiresReadback: true,
            resources: [secondObject, secondString]);
        GeometryDescription differentBounds = CreateDescription(
            RenderBoundsContract.FullInput,
            RenderHitTestContract.AnyInput,
            requiresReadback: true,
            resources: [secondObject, secondString]);
        GeometryDescription differentHitTest = CreateDescription(
            RenderBoundsContract.Identity,
            RenderHitTestContract.None,
            requiresReadback: true,
            resources: [secondObject, secondString]);
        GeometryDescription differentReadback = CreateDescription(
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            requiresReadback: false,
            resources: [secondObject, secondString]);
        GeometryDescription differentResourceOrder = CreateDescription(
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            requiresReadback: true,
            resources: [secondString, secondObject]);

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
            IEnumerable<RenderResource> resources)
        {
            return GeometryDescription.Create(
                static _ => { },
                bounds,
                hitTest,
                structuralKey: "geometry-shape",
                requiresReadback: requiresReadback,
                resources: resources);
        }
    }

    [Test]
    public void Session_UsesCompositionGlobalCanvasAndScopesEveryFacade()
    {
        var inputBounds = new Rect(8.5f, 17.25f, 10, 8);
        var outputBounds = new Rect(10.25f, 20.5f, 5, 4);
        const float density = 2;
        PixelRect deviceBounds = PixelRect.FromRect(outputBounds, density);
        var sessionToken = new RenderExecutionSessionToken();
        using var registry = new RenderRequestResourceRegistry();
        var raw = new object();
        RenderResource<object> resource = registry.RegisterBorrowed(raw, "geometry-resource", 2);
        registry.Commit(resource);
        var input = new RenderExecutionInput(
            sessionToken,
            inputBounds,
            EffectiveScale.At(1.5f),
            static (_, _) => { },
            static (_, _) => { },
            createShader: null,
            createSnapshot: null,
            readbackDeclared: false);
        using RenderTarget target = RenderTarget.CreateNull(deviceBounds.Width, deviceBounds.Height);
        var canvas = new RenderCallbackCanvas(
            sessionToken,
            density,
            outputBounds,
            () => new ImmediateCanvas(target, density, maxWorkingScale: 4, outputBounds.Size),
            CallbackCanvasCapability.Draw);
        var session = new GeometrySession(
            sessionToken,
            input,
            outputBounds,
            outputBounds,
            deviceBounds,
            outputScale: 1,
            workingScale: density,
            maxWorkingScale: 4,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            canvas,
            [resource]);

        Assert.Multiple(() =>
        {
            Assert.That(session.Input, Is.SameAs(input));
            Assert.That(session.OutputBounds, Is.EqualTo(outputBounds));
            Assert.That(session.RequiredRegion, Is.EqualTo(outputBounds));
            Assert.That(session.DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(session.DeviceSize, Is.EqualTo(deviceBounds.Size));
            Assert.That(session.OutputScale, Is.EqualTo(1));
            Assert.That(session.WorkingScale, Is.EqualTo(density));
            Assert.That(session.MaxWorkingScale, Is.EqualTo(4));
            Assert.That(session.Intent, Is.EqualTo(RenderIntent.Preview));
            Assert.That(session.Purpose, Is.EqualTo(RenderRequestPurpose.Frame));
        });

        bool resourceUsed = false;
        session.UseResource(resource, value => resourceUsed = ReferenceEquals(value, raw));
        session.Canvas.Use(immediate => session.Input.Draw(immediate));
        Assert.Multiple(() =>
        {
            Assert.That(resourceUsed, Is.True);
            Assert.That(() => session.Canvas.Use(static _ => { }), Throws.TypeOf<InvalidOperationException>());
        });

        sessionToken.Complete();
        Assert.Multiple(() =>
        {
            Assert.That(() => _ = session.Input, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = session.OutputBounds, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => session.UseResource(resource, static _ => { }), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => session.SetOutputBounds(outputBounds), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => session.DiscardOutput(), Throws.TypeOf<InvalidOperationException>());
        });
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

using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Every refusal a slot declaration owes the bindings addressed to it, asserted on the exception type,
/// the message, and the parameter each one names.
/// </summary>
/// <remarks>
/// These checks are performed once per element on the recording path, so which pass runs them moves as
/// that path is tightened. What must not move is what a caller sees: a doubly-bound slot, an undeclared
/// slot, a released resource and a null binding each stay refused as themselves rather than collapsing
/// into whichever fault the scan happens to notice first.
/// </remarks>
[TestFixture]
public sealed class DeclaredSlotBindingRefusalTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [Test]
    public void BindingOneDeclaredSlotTwice_IsRefused()
    {
        using var registry = new RenderRequestResourceRegistry();
        var bound = new RenderResourceSlot<Payload>();
        var neverBound = new RenderResourceSlot<Payload>();

        ArgumentException refusal = AssertRefuses(
            [bound, neverBound],
            [bound.Bind(Borrow(registry)), bound.Bind(Borrow(registry))]);

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal.Message,
                Does.StartWith("A render resource slot cannot be bound more than once."));
            Assert.That(refusal.ParamName, Is.EqualTo("resources"));
        });
    }

    /// <remarks>
    /// Past eight bindings the duplicate check stops being a scan and builds an index instead, so the
    /// refusal has a second implementation to answer for.
    /// </remarks>
    [Test]
    public void BindingOneDeclaredSlotTwice_IsRefusedPastTheLinearScanWidth()
    {
        const int Width = 12;
        using var registry = new RenderRequestResourceRegistry();
        var slots = new RenderResourceSlot[Width];
        var bindings = new RenderResourceBinding[Width];
        for (int index = 0; index < Width; index++)
        {
            var slot = new RenderResourceSlot<Payload>();
            slots[index] = slot;
            bindings[index] = slot.Bind(Borrow(registry));
        }

        bindings[Width - 1] = ((RenderResourceSlot<Payload>)slots[0]).Bind(Borrow(registry));

        ArgumentException refusal = AssertRefuses(slots, bindings);

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal.Message,
                Does.StartWith("A render resource slot cannot be bound more than once."));
            Assert.That(refusal.ParamName, Is.EqualTo("resources"));
        });
    }

    [Test]
    public void BindingASlotTheDescriptionDidNotDeclare_IsRefused()
    {
        using var registry = new RenderRequestResourceRegistry();
        var declared = new RenderResourceSlot<Payload>();
        var undeclared = new RenderResourceSlot<Payload>();

        ArgumentException refusal = AssertRefuses([declared], [undeclared.Bind(Borrow(registry))]);

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal.Message,
                Does.StartWith("A render description contains a resource slot it did not declare."));
            Assert.That(refusal.ParamName, Is.EqualTo("resources"));
        });
    }

    [Test]
    public void BindingAResourceReleasedAfterTheBindingWasMade_IsRefused()
    {
        using var registry = new RenderRequestResourceRegistry();
        var slot = new RenderResourceSlot<Payload>();
        RenderResource<Payload> resource = Borrow(registry);
        RenderResourceBinding binding = slot.Bind(resource);
        registry.Release(resource);

        ArgumentException refusal = AssertRefuses([slot], [binding]);

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal.Message,
                Does.StartWith("A released render resource cannot be declared."));
            Assert.That(refusal.ParamName, Is.EqualTo("resources"));
        });
    }

    [Test]
    public void SupplyingANullBinding_IsRefused()
    {
        var slot = new RenderResourceSlot<Payload>();

        ArgumentException refusal = AssertRefuses([slot], [null!]);

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal.Message,
                Does.StartWith("A declared render resource binding cannot be null."));
            Assert.That(refusal.ParamName, Is.EqualTo("resources"));
        });
    }

    [Test]
    public void LeavingADeclaredSlotUnbound_IsRefused()
    {
        using var registry = new RenderRequestResourceRegistry();
        var bound = new RenderResourceSlot<Payload>();
        var unbound = new RenderResourceSlot<Payload>();

        ArgumentException refusal = AssertRefuses([bound, unbound], [bound.Bind(Borrow(registry))]);

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal.Message,
                Does.StartWith("A render description must bind every resource slot it declares exactly once."));
            Assert.That(refusal.ParamName, Is.EqualTo("resources"));
        });
    }

    [Test]
    public void BindingAResourceWithoutDeclaringAnySlot_IsRefusedAgainstTheDeclaration()
    {
        using var registry = new RenderRequestResourceRegistry();
        var slot = new RenderResourceSlot<Payload>();

        ArgumentException refusal = AssertRefuses(null, [slot.Bind(Borrow(registry))]);

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal.Message,
                Does.StartWith("A render call that declares no resource slots cannot bind a resource."));
            Assert.That(refusal.ParamName, Is.EqualTo("slots"));
        });
    }

    /// <remarks>
    /// The control the refusals above are read against: a declaration every binding answers exactly once
    /// is accepted, and reaches the description in the order it was declared rather than bound.
    /// </remarks>
    [Test]
    public void BindingEveryDeclaredSlotExactlyOnce_IsAcceptedInDeclarationOrder()
    {
        using var registry = new RenderRequestResourceRegistry();
        var first = new RenderResourceSlot<Payload>();
        var second = new RenderResourceSlot<Payload>();
        RenderResourceBinding firstBinding = first.Bind(Borrow(registry));
        RenderResourceBinding secondBinding = second.Bind(Borrow(registry));

        OpaqueRenderDescription description = Describe([first, second], [secondBinding, firstBinding]);

        Assert.That(
            description.Resources.Select(static binding => binding.Slot),
            Is.EqualTo(new RenderResourceSlot[] { first, second }));
    }

    /// <remarks>
    /// The tests above reach the checks through <see cref="OpaqueRenderDescription.Create"/>, which copies
    /// and re-checks the bindings a second time on its way to the constructor. A painted source does not:
    /// it assembles its bindings itself and hands the engine factory the only reference to them, so the
    /// pass below is the sole thing standing between a caller's mistake and a recorded operation. These
    /// drive that path instead.
    /// </remarks>
    [Test]
    public void APaintedSourceRefusesABindingCarryingAReleasedResource()
    {
        using var registry = new RenderRequestResourceRegistry();
        var slot = new RenderResourceSlot<Payload>();
        RenderResource<Payload> resource = Borrow(registry);
        RenderResourceBinding binding = slot.Bind(resource);
        registry.Release(resource);

        ArgumentException refusal = AssertPaintedSourceRefuses(_ => ([slot], [binding]));

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal.Message,
                Does.StartWith("A released render resource cannot be declared."));
            Assert.That(refusal.ParamName, Is.EqualTo("bindings"));
        });
    }

    [Test]
    public void APaintedSourceRefusesOneSlotBoundTwice()
    {
        var bound = new RenderResourceSlot<Payload>();
        var neverBound = new RenderResourceSlot<Payload>();

        ArgumentException refusal = AssertPaintedSourceRefuses(context =>
            ([bound, neverBound],
             [bound.Bind(context.Borrow(new Payload())), bound.Bind(context.Borrow(new Payload()))]));

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal.Message,
                Does.StartWith("A render resource slot cannot be bound more than once."));
            Assert.That(refusal.ParamName, Is.EqualTo("bindings"));
        });
    }

    [Test]
    public void APaintedSourceRefusesANullBinding()
    {
        var slot = new RenderResourceSlot<Payload>();

        ArgumentException refusal = AssertPaintedSourceRefuses(_ => ([slot], [null!]));

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal.Message,
                Does.StartWith("A declared render resource binding cannot be null."));
            Assert.That(refusal.ParamName, Is.EqualTo("bindings"));
        });
    }

    [Test]
    public void APaintedSourceRefusesASlotItDidNotDeclare()
    {
        var declared = new RenderResourceSlot<Payload>();
        var undeclared = new RenderResourceSlot<Payload>();

        ArgumentException refusal = AssertPaintedSourceRefuses(context =>
            ([declared], [undeclared.Bind(context.Borrow(new Payload()))]));

        Assert.Multiple(() =>
        {
            Assert.That(
                refusal.Message,
                Does.StartWith("A render description contains a resource slot it did not declare."));
            Assert.That(refusal.ParamName, Is.EqualTo("bindings"));
        });
    }

    private static ArgumentException AssertPaintedSourceRefuses(
        Func<RenderNodeContext, (RenderResourceSlot[] Slots, RenderResourceBinding[] Bindings)> declare)
    {
        Exception? caught = null;
        using var node = new RefusalProbeNode(context =>
        {
            (RenderResourceSlot[] slots, RenderResourceBinding[] bindings) = declare(context);
            caught = Assert.Catch(() => context.PaintedSource(
                s_bounds,
                static (canvas, fill, pen, bounds) => canvas.DrawRectangle(bounds, fill, pen),
                null,
                null,
                s_bounds,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.Vector,
                bindings: bindings,
                slots: slots));

            // Leaves the recording well formed, so the refusal above is the only thing under test.
            context.Publish(context.PaintedSource(
                s_bounds,
                static (canvas, fill, pen, bounds) => canvas.DrawRectangle(bounds, fill, pen),
                null,
                null,
                s_bounds,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.Vector));
        });

        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));
        new RenderRequestRecorder(request).Record(node);

        Assert.That(caught, Is.TypeOf<ArgumentException>());
        return (ArgumentException)caught!;
    }

    private static RenderResource<Payload> Borrow(RenderRequestResourceRegistry registry)
        => registry.RegisterBorrowed(new Payload());

    private static OpaqueRenderDescription Describe(
        IEnumerable<RenderResourceSlot>? slots,
        IEnumerable<RenderResourceBinding>? bindings)
        => OpaqueRenderDescription.Create(
            s_bounds,
            static (session, bounds) =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(bounds);
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            resources: bindings,
            slots: slots);

    private static ArgumentException AssertRefuses(
        IEnumerable<RenderResourceSlot>? slots,
        IEnumerable<RenderResourceBinding>? bindings)
        => Assert.Throws<ArgumentException>(() => Describe(slots, bindings))!;

    private sealed class RefusalProbeNode(Action<RenderNodeContext> probe) : RenderNode
    {
        public override void Process(RenderNodeContext context) => probe(context);
    }

    private sealed class Payload;
}

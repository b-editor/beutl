using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class RenderResourceSlotTests
{
    [Test]
    public void BoundSlotsLeaseTheMatchingTypedResourceRegardlessOfBindingOrder()
    {
        var left = new Payload("left");
        var right = new Payload("right");
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<Payload> leftToken = registry.RegisterBorrowed(left);
        RenderResource<Payload> rightToken = registry.RegisterBorrowed(right);
        registry.Commit(leftToken);
        registry.Commit(rightToken);

        var leftSlot = new RenderResourceSlot<Payload>();
        var rightSlot = new RenderResourceSlot<Payload>();
        RenderResourceBinding[] bindings = [rightSlot.Bind(rightToken), leftSlot.Bind(leftToken)];
        var reached = new List<string>();
        var session = new RenderExecutionSessionToken();

        session.RunAndComplete(() =>
            session.UseResource(
                leftSlot,
                bindings,
                value =>
                {
                    reached.Add(value.Name);
                    session.UseResource(rightSlot, bindings, other => reached.Add(other.Name));
                }));

        Assert.That(reached, Is.EqualTo(new[] { "left", "right" }));
    }

    [Test]
    public void MissingSlotFailsWithoutFallingBackToAnotherSameTypedBinding()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<Payload> token = registry.RegisterBorrowed(new Payload("bound"));
        registry.Commit(token);

        var bound = new RenderResourceSlot<Payload>();
        var missing = new RenderResourceSlot<Payload>();
        RenderResourceBinding[] bindings = [bound.Bind(token)];
        var session = new RenderExecutionSessionToken();

        KeyNotFoundException? exception = Assert.Throws<KeyNotFoundException>(() =>
            session.RunAndComplete(() => session.UseResource(missing, bindings, static _ => { })));

        Assert.That(exception!.Message, Does.Contain("slot"));
    }

    [Test]
    public void BindingRejectsATokenWhoseLifecycleHasEnded()
    {
        var payload = new DisposablePayload();
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<DisposablePayload> token = registry.RegisterOwned(payload);
        var slot = new RenderResourceSlot<DisposablePayload>();

        registry.Rollback(token);

        Assert.Multiple(() =>
        {
            Assert.That(payload.DisposeCalls, Is.EqualTo(1));
            Assert.That(
                () => slot.Bind(token),
                Throws.InvalidOperationException.With.Message.Contains("cannot be bound"));
        });
    }

    [Test]
    public void BorrowedResourcesAreReleasedWithoutTakingDisposalOwnership()
    {
        var payload = new DisposablePayload();
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<DisposablePayload> token = registry.RegisterBorrowed(payload);

        registry.Rollback(token);

        Assert.That(payload.DisposeCalls, Is.Zero);
    }

    [Test]
    public void BindingRejectsATokenWithADifferentDeclaredType()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<Payload> token = registry.RegisterBorrowed(new Payload("payload"));
        var slot = new RenderResourceSlot<OtherPayload>();

        ArgumentException? exception = Assert.Throws<ArgumentException>(
            () => new RenderResourceBinding(slot, token));

        Assert.That(exception!.ParamName, Is.EqualTo("resource"));
    }

    private sealed class Payload(string name)
    {
        public string Name { get; } = name;
    }

    private sealed class OtherPayload;

    private sealed class DisposablePayload : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
    }
}

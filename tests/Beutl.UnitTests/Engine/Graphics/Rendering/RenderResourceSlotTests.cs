using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class RenderResourceSlotTests
{
    private const int AllocationIterations = 10_000;
    private static readonly Action<Payload> s_ignorePayload = static _ => { };

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
    public void TheSameRegistrationCanBeReadInsideItsOwnLease()
    {
        var payload = new Payload("shared");
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<Payload> token = registry.RegisterBorrowed(payload);
        registry.Commit(token);

        var slot = new RenderResourceSlot<Payload>();
        RenderResourceBinding[] bindings = [slot.Bind(token)];
        var reached = new List<string>();
        var session = new RenderExecutionSessionToken();

        Assert.That(
            () => session.RunAndComplete(() =>
                session.UseResource(
                    slot,
                    bindings,
                    outer =>
                    {
                        reached.Add("outer:" + outer.Name);
                        session.UseResource(
                            slot,
                            bindings,
                            inner => reached.Add("inner:" + inner.Name));
                    })),
            Throws.Nothing);

        Assert.That(reached, Is.EqualTo(new[] { "outer:shared", "inner:shared" }));
    }

    [Test]
    public void SlotAddressing_DoesNotAllocateAProjectedResourceList()
    {
        var payload = new Payload("payload");
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<Payload> token = registry.RegisterBorrowed(payload);
        registry.Commit(token);

        var slot = new RenderResourceSlot<Payload>();
        RenderResourceBinding[] bindings = [slot.Bind(token)];
        var tokenSession = new RenderExecutionSessionToken();
        var slotSession = new RenderExecutionSessionToken();
        Action tokenAccess = () => tokenSession.UseResource(token, bindings, s_ignorePayload);
        Action slotAccess = () => slotSession.UseResource(slot, bindings, s_ignorePayload);

        try
        {
            long tokenBytes = MeasureAllocatedBytes(tokenAccess);
            long slotBytes = MeasureAllocatedBytes(slotAccess);

            TestContext.Out.WriteLine($"token addressing: {tokenBytes / AllocationIterations} bytes/call");
            TestContext.Out.WriteLine($"slot addressing: {slotBytes / AllocationIterations} bytes/call");
            Assert.That(
                slotBytes,
                Is.LessThanOrEqualTo(tokenBytes),
                "resolving a typed slot must not project every binding into a temporary resource array");
        }
        finally
        {
            slotSession.Complete();
            tokenSession.Complete();
        }
    }

    [Test]
    public void SlotUse_RestoresAuthorizationAndLeaseAfterCallbackFailure()
    {
        var payload = new Payload("payload");
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<Payload> token = registry.RegisterBorrowed(payload);
        registry.Commit(token);

        var slot = new RenderResourceSlot<Payload>();
        RenderResourceBinding[] bindings = [slot.Bind(token)];
        var session = new RenderExecutionSessionToken();
        var failure = new InvalidOperationException("sentinel");
        bool authorizedInside = false;
        RenderResourceOwnershipState stateInside = default;

        try
        {
            InvalidOperationException? caught = Assert.Throws<InvalidOperationException>(() =>
                session.UseResource(
                    slot,
                    bindings,
                    value =>
                    {
                        authorizedInside = session.IsResourceAuthorized(value);
                        stateInside = token.OwnershipState;
                        throw failure;
                    }));

            Assert.Multiple(() =>
            {
                Assert.That(caught, Is.SameAs(failure));
                Assert.That(authorizedInside, Is.True);
                Assert.That(stateInside, Is.EqualTo(RenderResourceOwnershipState.LeasedToCallback));
                Assert.That(session.IsResourceAuthorized(payload), Is.False);
                Assert.That(token.OwnershipState, Is.EqualTo(RenderResourceOwnershipState.RequestBorrowed));
            });
        }
        finally
        {
            session.Complete();
        }
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

    private static long MeasureAllocatedBytes(Action action)
    {
        for (int index = 0; index < 100; index++)
            action();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < AllocationIterations; index++)
            action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
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

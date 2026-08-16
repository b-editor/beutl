using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public sealed class ElementSourceHandlerRegistryTests
{
    [Test]
    public async Task Register_OrdersHandlersAndRequiresExplicitReplacement()
    {
        await using var registry = new ElementSourceHandlerRegistry();
        var original = new TestHandler(typeof(FirstSource));
        var other = new TestHandler(typeof(SecondSource));
        await using IElementSourceHandlerRegistration originalRegistration = registry.Register(
            new ElementSourceHandlerRegistration(original, order: 20));
        await using IElementSourceHandlerRegistration otherRegistration = registry.Register(
            new ElementSourceHandlerRegistration(other, order: -10));

        Assert.That(registry.Handlers, Is.EqualTo(new[] { other, original }));
        Assert.Throws<ArgumentException>(() => registry.Register(
            new ElementSourceHandlerRegistration(new TestHandler(typeof(FirstSource)))));

        var replacement = new TestHandler(typeof(FirstSource));
        IElementSourceHandlerRegistration replacementRegistration = registry.Register(
            new ElementSourceHandlerRegistration(
                replacement,
                ElementSourceHandlerRegistrationMode.Replace,
                order: 0));

        Assert.That(registry.TryAcquire(typeof(FirstSource), out IElementSourceHandlerLease? lease), Is.True);
        using (lease)
        {
            Assert.That(lease!.Handler, Is.SameAs(replacement));
        }
        Assert.That(registry.Handlers, Is.EqualTo(new[] { other, replacement }));

        await replacementRegistration.DisposeAsync();
        Assert.That(registry.TryAcquire(typeof(FirstSource), out lease), Is.True);
        using (lease)
        {
            Assert.That(lease!.Handler, Is.SameAs(original));
        }
    }

    [Test]
    public async Task Register_ReplaceRequiresAnExistingHandler()
    {
        await using var registry = new ElementSourceHandlerRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(
            new ElementSourceHandlerRegistration(
                new TestHandler(typeof(FirstSource)),
                ElementSourceHandlerRegistrationMode.Replace)));
    }

    [Test]
    public async Task RegistrationDispose_RetiresBeforeWaitingAndDrainsActiveLeases()
    {
        await using var registry = new ElementSourceHandlerRegistry();
        var fallback = new TestHandler(typeof(FirstSource));
        var replacement = new TestHandler(typeof(FirstSource));
        await using IElementSourceHandlerRegistration fallbackRegistration = registry.Register(
            new ElementSourceHandlerRegistration(fallback));
        IElementSourceHandlerRegistration replacementRegistration = registry.Register(
            new ElementSourceHandlerRegistration(
                replacement,
                ElementSourceHandlerRegistrationMode.Replace));
        Assert.That(
            registry.TryAcquire(typeof(FirstSource), out IElementSourceHandlerLease? activeLease),
            Is.True);
        IElementSourceHandlerLease lease = activeLease!;

        Task disposeTask = replacementRegistration.DisposeAsync().AsTask();
        try
        {
            await WaitUntilAsync(IsFallbackActive, TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(disposeTask.IsCompleted, Is.False);
                Assert.That(lease.Handler, Is.SameAs(replacement));
            }
        }
        finally
        {
            lease.Dispose();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Throws<ObjectDisposedException>(() => _ = lease.Handler);

        bool IsFallbackActive()
        {
            if (!registry.TryAcquire(typeof(FirstSource), out IElementSourceHandlerLease? candidate))
                return false;

            using (candidate)
            {
                return ReferenceEquals(candidate!.Handler, fallback);
            }
        }
    }

    [Test]
    public async Task ExtensionRegistrations_AreComposedAndRetiredWhenPackageIsRemoved()
    {
        var provider = new ExtensionProvider();
        await using var registry = new ElementSourceHandlerRegistry([], provider);
        var handler = new TestHandler(typeof(FirstSource));
        var extension = new TestExtension(
        [
            new ElementSourceHandlerRegistration(handler),
        ]);

        provider.AddExtensions(1, [extension]);

        Assert.That(registry.TryAcquire(typeof(FirstSource), out IElementSourceHandlerLease? lease), Is.True);
        using (lease)
        {
            Assert.That(lease!.Handler, Is.SameAs(handler));
        }

        await provider.RemoveExtensions(1).DrainAsync();

        Assert.That(registry.TryAcquire(typeof(FirstSource), out lease), Is.False);
    }

    [Test]
    public async Task InvalidExtensionRegistration_RollsBackPartialContributions()
    {
        var provider = new ExtensionProvider();
        var failures = new List<ElementSourceHandlerExtensionFailure>();
        var hostHandler = new TestHandler(typeof(FirstSource));
        await using var registry = new ElementSourceHandlerRegistry(
        [
            new ElementSourceHandlerRegistration(hostHandler),
        ],
        provider,
        failures.Add);
        var extension = new TestExtension(
        [
            new ElementSourceHandlerRegistration(new TestHandler(typeof(SecondSource))),
            new ElementSourceHandlerRegistration(new TestHandler(typeof(FirstSource))),
        ]);

        provider.AddExtensions(2, [extension]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(registry.TryAcquire(typeof(SecondSource), out _), Is.False);
            Assert.That(registry.TryAcquire(typeof(FirstSource), out IElementSourceHandlerLease? lease), Is.True);
            using (lease)
            {
                Assert.That(lease!.Handler, Is.SameAs(hostHandler));
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        const int DelayMilliseconds = 10;
        int attempts = (int)Math.Ceiling(timeout.TotalMilliseconds / DelayMilliseconds);
        for (int index = 0; index < attempts; index++)
        {
            if (condition())
                return;

            await Task.Delay(DelayMilliseconds);
        }

        Assert.Fail("The expected registry state was not reached before the timeout.");
    }

    private sealed record FirstSource : ElementSource;

    private sealed record SecondSource : ElementSource;

    private sealed class TestHandler(Type sourceType) : IElementSourceHandler
    {
        public Type SourceType { get; } = sourceType;

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class TestExtension(
        IReadOnlyCollection<ElementSourceHandlerRegistration> registrations)
        : ElementSourceHandlerExtension
    {
        public override IReadOnlyCollection<ElementSourceHandlerRegistration> Registrations { get; } = registrations;
    }
}

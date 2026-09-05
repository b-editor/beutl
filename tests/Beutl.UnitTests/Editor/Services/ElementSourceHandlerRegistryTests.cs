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
    public async Task ExtensionRegistrations_ComposeReplacementEnumeratedBeforeItsBase()
    {
        var provider = new ExtensionProvider();
        var failures = new List<ElementSourceHandlerExtensionFailure>();
        await using var registry = new ElementSourceHandlerRegistry([], provider, failures.Add);
        var baseHandler = new TestHandler(typeof(FirstSource));
        var replacementHandler = new TestHandler(typeof(FirstSource));
        var replacement = new TestExtension(
        [
            new ElementSourceHandlerRegistration(
                replacementHandler,
                ElementSourceHandlerRegistrationMode.Replace),
        ]);
        var baseExtension = new TestExtension(
        [
            new ElementSourceHandlerRegistration(baseHandler),
        ]);

        provider.AddExtensions(3, [replacement, baseExtension]);

        Assert.That(registry.TryAcquire(
            typeof(FirstSource),
            out IElementSourceHandlerLease? lease), Is.True);
        using (lease)
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lease!.Handler, Is.SameAs(replacementHandler));
            Assert.That(replacement.RegistrationsReadCount, Is.EqualTo(1));
            Assert.That(baseExtension.RegistrationsReadCount, Is.EqualTo(1));
            Assert.That(failures, Is.Empty);
        }
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

    [Test]
    public async Task InvalidProvisionalRegistration_DoesNotShadowAValidExtension()
    {
        var provider = new ExtensionProvider();
        var failures = new List<ElementSourceHandlerExtensionFailure>();
        await using var registry = new ElementSourceHandlerRegistry([], provider, failures.Add);
        var invalidHandler = new TestHandler(typeof(FirstSource));
        var validHandler = new TestHandler(typeof(FirstSource));
        var invalidExtension = new TestExtension(
        [
            new ElementSourceHandlerRegistration(invalidHandler),
            new ElementSourceHandlerRegistration(
                new TestHandler(typeof(SecondSource)),
                ElementSourceHandlerRegistrationMode.Replace),
        ]);
        var validExtension = new TestExtension(
        [
            new ElementSourceHandlerRegistration(validHandler),
        ]);

        provider.AddExtensions(5, [invalidExtension, validExtension]);

        Assert.That(registry.TryAcquire(typeof(FirstSource), out IElementSourceHandlerLease? lease), Is.True);
        using (lease)
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lease!.Handler, Is.SameAs(validHandler));
            Assert.That(registry.TryAcquire(typeof(SecondSource), out _), Is.False);
            Assert.That(failures, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task InvalidExtensionNeverPublishesItsPartialRegistration()
    {
        var provider = new ExtensionProvider();
        var failures = new List<ElementSourceHandlerExtensionFailure>();
        await using var registry = new ElementSourceHandlerRegistry([], provider, failures.Add);
        using var invalidGetterEntered = new ManualResetEventSlim();
        using var releaseInvalidGetter = new ManualResetEventSlim();
        var extension = new TestExtension(
        [
            new ElementSourceHandlerRegistration(new TestHandler(typeof(FirstSource))),
            new ElementSourceHandlerRegistration(new BlockingInvalidHandler(
                invalidGetterEntered,
                releaseInvalidGetter)),
        ]);

        Task addition = Task.Run(() => provider.AddExtensions(4, [extension]));
        Assert.That(invalidGetterEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        Task<bool> acquisition = Task.Run(() =>
        {
            bool acquired = registry.TryAcquire(
                typeof(FirstSource),
                out IElementSourceHandlerLease? lease);
            lease?.Dispose();
            return acquired;
        });
        Assert.That(
            await acquisition.WaitAsync(TimeSpan.FromSeconds(2)),
            Is.False,
            "Plugin getters must run before the registry gate is acquired.");

        releaseInvalidGetter.Set();
        await addition.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(acquisition.Result, Is.False);
            Assert.That(failures, Has.Count.EqualTo(1));
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

    private sealed class BlockingInvalidHandler(
        ManualResetEventSlim entered,
        ManualResetEventSlim release) : IElementSourceHandler
    {
        public Type SourceType
        {
            get
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The invalid handler getter was not released.");
                return typeof(string);
            }
        }

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

    private sealed class TestExtension : ElementSourceHandlerExtension
    {
        private readonly IReadOnlyCollection<ElementSourceHandlerRegistration> _registrations;

        public TestExtension(IReadOnlyCollection<ElementSourceHandlerRegistration> registrations)
        {
            _registrations = registrations;
        }

        public int RegistrationsReadCount { get; private set; }

        public override IReadOnlyCollection<ElementSourceHandlerRegistration> Registrations
        {
            get
            {
                RegistrationsReadCount++;
                return _registrations;
            }
        }
    }
}

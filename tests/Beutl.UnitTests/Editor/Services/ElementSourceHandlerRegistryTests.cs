using System.Collections;
using System.Collections.Immutable;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public sealed class ElementSourceHandlerRegistryTests
{
    [Test]
    public void MaterializationSnapshotsOuterAndInnerGroupsWithTheHostComparer()
    {
        var primary = new Element();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var supplied = new HashSet<Guid>(new DelegatingGuidComparer()) { first, second };
        var inner = new SingleUseReadOnlySet<Guid>(supplied);
        var outer = new SingleUseEnumerable<IReadOnlySet<Guid>>([inner]);
        var materialization = new ElementMaterialization(
            primary,
            groups: outer);

        supplied.Clear();
        supplied.Add(Guid.NewGuid());

        var snapshot = (ImmutableHashSet<Guid>)materialization.Groups.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Is.EquivalentTo(new[] { first, second }));
            Assert.That(snapshot.KeyComparer, Is.SameAs(EqualityComparer<Guid>.Default));
            Assert.Throws<ArgumentException>(() => new ElementMaterialization(
                primary,
                groups: [null!]));
        }
    }

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

        Assert.That(
            registry.Handlers.Select(handler => handler.SourceTypeName),
            Is.EqualTo(new[]
            {
                typeof(SecondSource).AssemblyQualifiedName,
                typeof(FirstSource).AssemblyQualifiedName,
            }));
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
        Assert.That(
            registry.Handlers.Select(handler => handler.Order),
            Is.EqualTo(new[] { -10, 0 }));

        await replacementRegistration.DisposeAsync();
        Assert.That(registry.TryAcquire(typeof(FirstSource), out lease), Is.True);
        using (lease)
        {
            Assert.That(lease!.Handler, Is.SameAs(original));
        }
    }

    [Test]
    public async Task Handlers_IsStableAndNotifiesOncePerVisibleDirectMutation()
    {
        await using var registry = new ElementSourceHandlerRegistry();
        var handlers = registry.Handlers;
        var snapshots = new List<int[]>();
        handlers.CollectionChanged += (_, _) =>
            snapshots.Add(handlers.Select(handler => handler.Order).ToArray());

        IElementSourceHandlerRegistration original = registry.Register(
            new ElementSourceHandlerRegistration(
                new TestHandler(typeof(FirstSource)),
                order: 20));
        IElementSourceHandlerRegistration replacement = registry.Register(
            new ElementSourceHandlerRegistration(
                new TestHandler(typeof(FirstSource)),
                ElementSourceHandlerRegistrationMode.Replace,
                order: -10));
        await replacement.DisposeAsync();
        await original.DisposeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.Handlers, Is.SameAs(handlers));
            Assert.That(snapshots, Is.EqualTo(new[]
            {
                new[] { 20 },
                new[] { -10 },
                new[] { 20 },
                Array.Empty<int>(),
            }));
            Assert.That(handlers, Is.Empty);
        }
    }

    [Test]
    public async Task Handlers_DoesNotNotifyWhenOnlyTheImplementationChanges()
    {
        await using var registry = new ElementSourceHandlerRegistry();
        int metadataChanges = 0;
        registry.Handlers.CollectionChanged += (_, _) => metadataChanges++;
        var original = new TestHandler(typeof(FirstSource));
        var replacement = new TestHandler(typeof(FirstSource));
        IElementSourceHandlerRegistration originalRegistration = registry.Register(
            new ElementSourceHandlerRegistration(original));
        IElementSourceHandlerRegistration replacementRegistration = registry.Register(
            new ElementSourceHandlerRegistration(
                replacement,
                ElementSourceHandlerRegistrationMode.Replace));

        Assert.That(registry.TryAcquire(
            typeof(FirstSource),
            out IElementSourceHandlerLease? replacementLease), Is.True);
        using (replacementLease)
        {
            Assert.That(replacementLease!.Handler, Is.SameAs(replacement));
        }
        await replacementRegistration.DisposeAsync();
        Assert.That(registry.TryAcquire(
            typeof(FirstSource),
            out IElementSourceHandlerLease? originalLease), Is.True);
        using (originalLease)
        {
            Assert.That(originalLease!.Handler, Is.SameAs(original));
        }

        Assert.That(metadataChanges, Is.EqualTo(1));
        await originalRegistration.DisposeAsync();
        Assert.That(metadataChanges, Is.EqualTo(2));
    }

    [Test]
    public async Task HandlerMetadataObserverFailureDoesNotRollBackRegistryMutation()
    {
        await using var registry = new ElementSourceHandlerRegistry();
        registry.Handlers.CollectionChanged += (_, _) =>
            throw new InvalidOperationException("observer failure");
        IElementSourceHandlerRegistration? registration = null;

        Assert.DoesNotThrow(() => registration = registry.Register(
            new ElementSourceHandlerRegistration(new TestHandler(typeof(FirstSource)))));
        Assert.That(registry.TryAcquire(
            typeof(FirstSource),
            out IElementSourceHandlerLease? lease), Is.True);
        lease!.Dispose();
        Assert.DoesNotThrowAsync(async () => await registration!.DisposeAsync());
        Assert.That(registry.Handlers, Is.Empty);
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
            new ElementSourceHandlerRegistration(fallback, order: 10));
        IElementSourceHandlerRegistration replacementRegistration = registry.Register(
            new ElementSourceHandlerRegistration(
                replacement,
                ElementSourceHandlerRegistrationMode.Replace,
                order: -10));
        Assert.That(
            registry.TryAcquire(typeof(FirstSource), out IElementSourceHandlerLease? activeLease),
            Is.True);
        IElementSourceHandlerLease lease = activeLease!;
        int metadataChanges = 0;
        registry.Handlers.CollectionChanged += (_, _) => metadataChanges++;

        Task disposeTask = replacementRegistration.DisposeAsync().AsTask();
        try
        {
            await WaitUntilAsync(IsFallbackActive, TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(disposeTask.IsCompleted, Is.False);
                Assert.That(lease.Handler, Is.SameAs(replacement));
                Assert.That(registry.Handlers.Single().Order, Is.EqualTo(10));
                Assert.That(metadataChanges, Is.EqualTo(1));
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
    public async Task RegistryDisposalJoinsAConcurrentDirectRegistrationRetirement()
    {
        var registry = new ElementSourceHandlerRegistry();
        IElementSourceHandlerRegistration registration = registry.Register(
            new ElementSourceHandlerRegistration(new TestHandler(typeof(FirstSource))));
        Assert.That(
            registry.TryAcquire(typeof(FirstSource), out IElementSourceHandlerLease? lease),
            Is.True);

        Task registrationDisposal = registration.DisposeAsync().AsTask();
        Assert.That(registrationDisposal.IsCompleted, Is.False);
        Task registryDisposal = registry.DisposeAsync().AsTask();
        Assert.That(registryDisposal.IsCompleted, Is.False);

        lease!.Dispose();
        await Task.WhenAll(registrationDisposal, registryDisposal)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ReentrantRegistryDisposalFromMetadataObserverStillWaitsForActiveLease()
    {
        await using var registry = new ElementSourceHandlerRegistry();
        IElementSourceHandlerRegistration registration = registry.Register(
            new ElementSourceHandlerRegistration(new TestHandler(typeof(FirstSource))));
        Assert.That(registry.TryAcquire(
            typeof(FirstSource),
            out IElementSourceHandlerLease? lease), Is.True);
        Task? reentrantDisposal = null;
        registry.Handlers.CollectionChanged += (_, _) =>
            reentrantDisposal ??= registry.DisposeAsync().AsTask();

        Task registrationDisposal = registration.DisposeAsync().AsTask();
        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(registrationDisposal.IsCompleted, Is.False);
                Assert.That(reentrantDisposal, Is.Not.Null);
                Assert.That(reentrantDisposal!.IsCompleted, Is.False);
            }
        }
        finally
        {
            lease!.Dispose();
        }

        await Task.WhenAll(registrationDisposal, reentrantDisposal!)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ExtensionRegistrations_AreComposedAndRetiredWhenPackageIsRemoved()
    {
        var provider = new ExtensionProvider();
        await using var registry = new ElementSourceHandlerRegistry([], provider);
        var handlers = registry.Handlers;
        int metadataChanges = 0;
        handlers.CollectionChanged += (_, _) => metadataChanges++;
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.TryAcquire(typeof(FirstSource), out lease), Is.False);
            Assert.That(registry.Handlers, Is.SameAs(handlers));
            Assert.That(handlers, Is.Empty);
            Assert.That(metadataChanges, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task ExtensionRemovalRetiresMetadataBeforeActiveLeaseDrainCompletes()
    {
        var provider = new ExtensionProvider();
        await using var registry = new ElementSourceHandlerRegistry([], provider);
        var handler = new TestHandler(typeof(FirstSource));
        provider.AddExtensions(12,
        [
            new TestExtension(
            [
                new ElementSourceHandlerRegistration(handler),
            ]),
        ]);
        Assert.That(registry.TryAcquire(
            typeof(FirstSource),
            out IElementSourceHandlerLease? lease), Is.True);

        ExtensionRemoval removal = provider.RemoveExtensions(12);
        Task drain = removal.DrainAsync().AsTask();
        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(registry.Handlers, Is.Empty);
                Assert.That(registry.TryAcquire(typeof(FirstSource), out _), Is.False);
                Assert.That(drain.IsCompleted, Is.False);
                Assert.That(lease!.Handler, Is.SameAs(handler));
            }
        }
        finally
        {
            lease!.Dispose();
        }

        await drain.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task FailedProviderAdditionDoesNotPublishHandlerMetadata()
    {
        var provider = new ExtensionProvider();
        await using var registry = new ElementSourceHandlerRegistry([], provider);
        int metadataChanges = 0;
        registry.Handlers.CollectionChanged += (_, _) => metadataChanges++;
        provider.AllExtensions.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                throw new InvalidOperationException("composition failure");
        };
        var extension = new TestExtension(
        [
            new ElementSourceHandlerRegistration(new TestHandler(typeof(FirstSource))),
        ]);

        var failure = Assert.Throws<ExtensionRegistrationNotificationException>(() =>
            provider.AddExtensions(11, [extension]));
        await failure!.Removal.DrainAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.Handlers, Is.Empty);
            Assert.That(metadataChanges, Is.Zero);
            Assert.That(registry.TryAcquire(typeof(FirstSource), out _), Is.False);
        }
    }

    [Test]
    public async Task ExtensionRegistrations_ComposeReplacementEnumeratedBeforeItsBase()
    {
        var provider = new ExtensionProvider();
        var failures = new List<ElementSourceHandlerExtensionFailure>();
        await using var registry = new ElementSourceHandlerRegistry([], provider, failures.Add);
        int metadataChanges = 0;
        registry.Handlers.CollectionChanged += (_, _) => metadataChanges++;
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
            Assert.That(metadataChanges, Is.EqualTo(1));
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
        int metadataChanges = 0;
        registry.Handlers.CollectionChanged += (_, _) => metadataChanges++;
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
            Assert.That(registry.Handlers.Select(handler => handler.SourceTypeName),
                Is.EqualTo(new[] { typeof(FirstSource).AssemblyQualifiedName }));
            Assert.That(metadataChanges, Is.Zero);
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
        var metadataSnapshots = new List<string[]>();
        registry.Handlers.CollectionChanged += (_, _) => metadataSnapshots.Add(
            registry.Handlers.Select(handler => handler.SourceTypeName).ToArray());

        provider.AddExtensions(5, [invalidExtension, validExtension]);

        Assert.That(registry.TryAcquire(typeof(FirstSource), out IElementSourceHandlerLease? lease), Is.True);
        using (lease)
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lease!.Handler, Is.SameAs(validHandler));
            Assert.That(registry.TryAcquire(typeof(SecondSource), out _), Is.False);
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(metadataSnapshots, Has.Count.EqualTo(1));
            Assert.That(metadataSnapshots.Single(),
                Is.EqualTo(new[] { typeof(FirstSource).AssemblyQualifiedName }));
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

    private sealed class DelegatingGuidComparer : IEqualityComparer<Guid>
    {
        public bool Equals(Guid x, Guid y) => x.Equals(y);

        public int GetHashCode(Guid obj) => obj.GetHashCode();
    }

    private sealed class SingleUseEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        private int _enumerations;

        public IEnumerator<T> GetEnumerator()
        {
            if (Interlocked.Increment(ref _enumerations) != 1)
                throw new InvalidOperationException("The outer collection was enumerated twice.");
            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SingleUseReadOnlySet<T>(IReadOnlySet<T> values) : IReadOnlySet<T>
    {
        private int _enumerations;

        public int Count => values.Count;

        public bool Contains(T item) => values.Contains(item);

        public bool IsProperSubsetOf(IEnumerable<T> other) => values.IsProperSubsetOf(other);

        public bool IsProperSupersetOf(IEnumerable<T> other) => values.IsProperSupersetOf(other);

        public bool IsSubsetOf(IEnumerable<T> other) => values.IsSubsetOf(other);

        public bool IsSupersetOf(IEnumerable<T> other) => values.IsSupersetOf(other);

        public bool Overlaps(IEnumerable<T> other) => values.Overlaps(other);

        public bool SetEquals(IEnumerable<T> other) => values.SetEquals(other);

        public IEnumerator<T> GetEnumerator()
        {
            if (Interlocked.Increment(ref _enumerations) != 1)
                throw new InvalidOperationException("The inner set was enumerated twice.");
            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

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

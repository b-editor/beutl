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
    public async Task ExtensionRemovalRetiresTheHandlerBeforeActiveLeaseDrainCompletes()
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
    public async Task FailedProviderAdditionDoesNotPublishTheHandler()
    {
        var provider = new ExtensionProvider();
        await using var registry = new ElementSourceHandlerRegistry([], provider);
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

        Assert.That(registry.TryAcquire(typeof(FirstSource), out _), Is.False);
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

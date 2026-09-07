using System.Collections.Specialized;
using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class ExtensionProviderTests
{
    private sealed class StubExtension : Extension;

    private sealed class OtherStubExtension : Extension;

    [Test]
    public void ExtensionProvider_ImplementsMutableRegistryAbstraction()
    {
        var provider = new ExtensionProvider();

        Assert.That(provider, Is.InstanceOf<IExtensionRegistry>());
        Assert.That(provider, Is.InstanceOf<IExtensionProvider>());
    }

    [Test]
    public void IExtensionProvider_ExtensionsExposeCopiedMetadataOnly()
    {
        var provider = new ExtensionProvider();
        IExtensionProvider abstraction = provider;

        Assert.That(abstraction.Extensions, Is.Empty);

        var ext = new StubExtension();
        provider.AddExtensions(1, [ext]);

        ExtensionDescriptor descriptor = abstraction.Extensions.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(descriptor.TypeName, Is.EqualTo(typeof(StubExtension).AssemblyQualifiedName));
            Assert.That(descriptor, Is.Not.SameAs(ext));
        }
    }

    [Test]
    public void IExtensionProvider_GetDescriptorsFiltersByTypeAndRequiresLeaseForExecution()
    {
        var provider = new ExtensionProvider();
        IExtensionProvider abstraction = provider;

        var first = new StubExtension();
        var second = new OtherStubExtension();
        provider.AddExtensions(1, [first]);
        provider.AddExtensions(2, [second]);

        ExtensionDescriptor[] matched = abstraction.GetDescriptors<StubExtension>().ToArray();

        Assert.That(matched, Has.Length.EqualTo(1));
        Assert.That(
            abstraction.TryAcquire(
                matched[0].Id,
                out IExtensionLease<StubExtension>? lease),
            Is.True);
        using (lease)
        {
            Assert.That(lease!.Extension, Is.SameAs(first));
        }
    }

    [Test]
    public void IExtensionProvider_TryAcquireReturnsFalseForUnknownDescriptor()
    {
        IExtensionProvider abstraction = new ExtensionProvider();

        Assert.That(
            abstraction.TryAcquire(
                new ExtensionId(Guid.NewGuid()),
                out IExtensionLease<StubExtension>? lease),
            Is.False);
        Assert.That(lease, Is.Null);
    }

    [Test]
    public void IExtensionProvider_ChangeNotificationPublishesOnlyCommittedMetadata()
    {
        var provider = new ExtensionProvider();
        IExtensionProvider abstraction = provider;
        var extension = new StubExtension();
        var snapshots = new List<(ExtensionDescriptor[] Descriptors, bool CanAcquire)>();
        ExtensionId extensionId = default;
        abstraction.ExtensionsChanged += (_, _) =>
        {
            ExtensionDescriptor[] descriptors = abstraction.Extensions.ToArray();
            if (descriptors.Length > 0)
            {
                extensionId = descriptors.Single().Id;
            }

            bool canAcquire = abstraction.TryAcquire(
                extensionId,
                out IExtensionLease<StubExtension>? lease);
            lease?.Dispose();
            snapshots.Add((descriptors, canAcquire));
        };

        provider.AddExtensions(1, [extension]);
        _ = provider.RemoveExtensions(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshots, Has.Count.EqualTo(2));
            Assert.That(snapshots[0].Descriptors.Single().TypeName,
                Is.EqualTo(typeof(StubExtension).AssemblyQualifiedName));
            Assert.That(snapshots[0].CanAcquire, Is.True);
            Assert.That(snapshots[1].Descriptors, Is.Empty);
            Assert.That(snapshots[1].CanAcquire, Is.False);
        }
    }

    [Test]
    public void IExtensionProvider_ObserverFailureCannotAbortPackageMutation()
    {
        var provider = new ExtensionProvider();
        IExtensionProvider abstraction = provider;
        abstraction.ExtensionsChanged += (_, _) =>
            throw new InvalidOperationException("public observer failure");

        Assert.DoesNotThrow(() => provider.AddExtensions(1, [new StubExtension()]));
        Assert.That(abstraction.Extensions, Has.Count.EqualTo(1));
        Assert.DoesNotThrow(() => _ = provider.RemoveExtensions(1));
        Assert.That(abstraction.Extensions, Is.Empty);
    }

    [Test]
    public async Task IExtensionProvider_FailedInternalCompositionNeverPublishesProvisionalMetadata()
    {
        var provider = new ExtensionProvider();
        IExtensionProvider abstraction = provider;
        using var observerStarted = new ManualResetEventSlim();
        using var releaseObserver = new ManualResetEventSlim();
        int publicNotifications = 0;
        abstraction.ExtensionsChanged += (_, _) => publicNotifications++;
        provider.AllExtensions.CollectionChanged += (_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Add)
                return;

            observerStarted.Set();
            if (!releaseObserver.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The composition observer was not released.");
            throw new InvalidOperationException("internal composition failure");
        };

        Task<ExtensionRegistrationNotificationException?> registration = Task.Run(() =>
            Assert.Throws<ExtensionRegistrationNotificationException>(() =>
                provider.AddExtensions(1, [new StubExtension()])));
        Assert.That(observerStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(abstraction.Extensions, Is.Empty);
                Assert.That(abstraction.GetDescriptors<StubExtension>(), Is.Empty);
                Assert.That(publicNotifications, Is.Zero);
            }
        }
        finally
        {
            releaseObserver.Set();
        }

        Assert.That(await registration.WaitAsync(TimeSpan.FromSeconds(5)), Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(abstraction.Extensions, Is.Empty);
            Assert.That(publicNotifications, Is.Zero);
        }
    }

    [Test]
    public void AddExtensions_ObserverFailureRollsBackAuthoritativeState()
    {
        var provider = new ExtensionProvider();
        provider.AllExtensions.CollectionChanged += (_, _) =>
            throw new InvalidOperationException("observer failure");

        var exception = Assert.Throws<ExtensionRegistrationNotificationException>(() =>
            provider.AddExtensions(1, [new StubExtension()]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.InnerException, Is.InstanceOf<Exception>());
            Assert.That(provider.GetExtensions<StubExtension>(), Is.Empty);
            Assert.That(provider.GetPackageExtensions(1), Is.Empty);
        }
    }

    [Test]
    public async Task AddExtensions_ObserverFailureProvidesDrainTicketForRetiredLeases()
    {
        var provider = new ExtensionProvider();
        var extension = new StubExtension();
        var releaseLease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        provider.AllExtensions.CollectionChanged += (_, _) =>
            throw new InvalidOperationException("observer failure");
        var registered = false;
        var observedRemoval = false;
        provider.AllExtensions.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
            {
                registered = true;
            }
            else if (args.Action == NotifyCollectionChangedAction.Remove && registered)
            {
                observedRemoval = true;
                ExtensionRegistrationLifetimes.Retire(
                    extension,
                    () => new ValueTask(releaseLease.Task));
            }
        };

        var exception = Assert.Throws<ExtensionRegistrationNotificationException>(() =>
            provider.AddExtensions(1, [extension]));
        Task drain = exception!.Removal.DrainAsync().AsTask();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registered, Is.True);
            Assert.That(observedRemoval, Is.True);
            Assert.That(drain.IsCompleted, Is.False);
        }
        releaseLease.SetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task RemoveExtensions_ObserverFailureStillSealsDrainTicket()
    {
        var provider = new ExtensionProvider();
        var extension = new StubExtension();
        provider.AddExtensions(1, [extension]);
        provider.AllExtensions.CollectionChanged += (_, _) =>
            throw new InvalidOperationException("observer failure");

        var exception = Assert.Throws<ExtensionRemovalNotificationException>(() =>
            provider.RemoveExtensions(1));

        Assert.That(exception, Is.Not.Null);
        await exception!.Removal.DrainAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Removal.Extensions, Is.EqualTo(new[] { extension }));
            Assert.That(provider.GetExtensions<StubExtension>(), Is.Empty);
            Assert.That(provider.GetPackageExtensions(1), Is.Empty);
        }
    }
}

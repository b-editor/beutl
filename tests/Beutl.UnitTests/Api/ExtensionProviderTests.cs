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
    public void IExtensionProvider_AllExtensions_ReflectsAddedExtensions()
    {
        var provider = new ExtensionProvider();
        IExtensionProvider abstraction = provider;

        Assert.That(abstraction.AllExtensions, Is.Empty);

        var ext = new StubExtension();
        provider.AddExtensions(1, [ext]);

        Assert.That(abstraction.AllExtensions, Has.Member(ext));
    }

    [Test]
    public void IExtensionProvider_GetExtensions_FiltersByType()
    {
        var provider = new ExtensionProvider();
        IExtensionProvider abstraction = provider;

        var first = new StubExtension();
        var second = new OtherStubExtension();
        provider.AddExtensions(1, [first]);
        provider.AddExtensions(2, [second]);

        StubExtension[] matched = abstraction.GetExtensions<StubExtension>();

        Assert.That(matched, Is.EquivalentTo(new[] { first }));
    }

    [Test]
    public void IExtensionProvider_MatchEditorExtension_ReturnsNullForUnknownFile()
    {
        IExtensionProvider abstraction = new ExtensionProvider();

        Assert.That(abstraction.MatchEditorExtension("unknown.bogus"), Is.Null);
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

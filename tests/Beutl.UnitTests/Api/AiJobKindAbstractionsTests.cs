using Beutl.Api.Services;
using Beutl.Editor.Services.AI;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class AiJobKindAbstractionsTests
{
    [Test]
    public void PluginContracts_SeparateServerJobKindsFromEditorResultCapabilities()
    {
        System.Reflection.Assembly contracts = typeof(AiJobStatusResolverExtension).Assembly;
        System.Reflection.Assembly editorContracts = typeof(IAiJobPresenter).Assembly;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contracts.GetName().Name, Is.EqualTo("Beutl.Api"));
            Assert.That(typeof(AiJobStatusResolverRegistration).Assembly, Is.SameAs(contracts));
            Assert.That(typeof(AiJobRefreshHandlerRegistration).Assembly, Is.SameAs(contracts));
            Assert.That(typeof(AiJobRetryHandlerRegistration).Assembly, Is.SameAs(contracts));
            Assert.That(typeof(AiJobStatusSemantics).Assembly, Is.SameAs(contracts));
            Assert.That(typeof(AiJob).Assembly, Is.SameAs(contracts));
            Assert.That(editorContracts.GetName().Name, Is.EqualTo("Beutl.Editor"));
            Assert.That(typeof(IAiJobCompletionPresenter).Assembly, Is.SameAs(editorContracts));
            Assert.That(typeof(IAiJobResultApplicator).Assembly, Is.SameAs(editorContracts));
            Assert.That(typeof(IAiJobResultContext).Assembly, Is.SameAs(editorContracts));
            Assert.That(typeof(IAiJobResultEditorContext).Assembly, Is.SameAs(editorContracts));
            Assert.That(typeof(AiJobKindRegistry).Assembly, Is.SameAs(contracts));
            Assert.That(contracts.GetType("Beutl.Api.Services.AiJobKindDescriptor"), Is.Null);
            Assert.That(contracts.GetType("Beutl.Api.Services.AiJobKindExtension"), Is.Null);
            Assert.That(contracts.GetType("Beutl.Api.Services.AiJobKindRegistrationMode"), Is.Null);
            Assert.That(contracts.GetType("Beutl.Api.Services.IAiJobKindRegistration"), Is.Null);
            Assert.That(contracts.GetType("Beutl.Api.Services.IAiJobKindLease"), Is.Null);
            Assert.That(contracts.GetType("Beutl.Api.Services.IAiJobResultHandler"), Is.Null);
            Assert.That(editorContracts.GetType("Beutl.Editor.Services.AI.IAiJobResultHandler"), Is.Null);
            Assert.That(contracts.GetType("Beutl.Api.Services.IAiJobResultDispatcher"), Is.Null);
            Assert.That(contracts.GetType("Beutl.Api.Services.IAiJobPresentationProvider"), Is.Null);
            Assert.That(contracts.GetType("Beutl.Api.Services.IAiJobCompletionHandler"), Is.Null);
            Assert.That(contracts.GetType("Beutl.Api.Services.AiJobPresentation"), Is.Null);
            Assert.That(contracts.GetType("Beutl.Api.Services.AiJobCompletionPresentation"), Is.Null);
            Assert.That(
                typeof(IAiJobRetryHandler)
                    .GetMethods()
                    .SelectMany(method => method.GetParameters())
                    .Select(parameter => parameter.ParameterType),
                Does.Not.Contain(typeof(IServiceProvider)));
        }
    }

    [Test]
    public async Task RegistryDisposalRetiresAndDrainsDirectRegistrations()
    {
        var registry = new AiJobKindRegistry();
        IAiJobStatusResolverRegistration registration = registry.Register(
            new AiJobStatusResolverRegistration(
            new AiJobKindId("tests.direct-disposal"),
            new AiJobStatusMap([])));
        Assert.That(registry.TryAcquireStatusResolver(
            new AiJobKindId("tests.direct-disposal"),
            out IAiJobStatusResolverLease? lease), Is.True);

        Task disposal = registry.DisposeAsync().AsTask();
        Assert.That(disposal.IsCompleted, Is.False);

        lease!.Dispose();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotThrowAsync(async () => await registration.DisposeAsync());
    }

    [Test]
    public async Task RegistryDisposalJoinsAConcurrentDirectRegistrationRetirement()
    {
        var registry = new AiJobKindRegistry();
        IAiJobStatusResolverRegistration registration = registry.Register(
            new AiJobStatusResolverRegistration(
            new AiJobKindId("tests.concurrent-direct-disposal"),
            new AiJobStatusMap([])));
        Assert.That(registry.TryAcquireStatusResolver(
            new AiJobKindId("tests.concurrent-direct-disposal"),
            out IAiJobStatusResolverLease? lease), Is.True);

        Task registrationDisposal = registration.DisposeAsync().AsTask();
        Assert.That(registrationDisposal.IsCompleted, Is.False);
        Task registryDisposal = registry.DisposeAsync().AsTask();
        Assert.That(registryDisposal.IsCompleted, Is.False);

        lease!.Dispose();
        await Task.WhenAll(registrationDisposal, registryDisposal)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ResultRegistryDisposalJoinsAConcurrentApplicatorRetirement()
    {
        var registry = new AiJobResultRegistry([], [], []);
        IAiJobResultApplicatorRegistration registration = registry.Register(
            new AiJobResultApplicatorRegistration(
                new AiJobKindId("tests.concurrent-result-disposal"),
                new TestCapabilities("application")));
        Assert.That(registry.TryAcquireApplicator(
            new AiJobKindId("tests.concurrent-result-disposal"),
            out IAiJobResultApplicatorLease? lease), Is.True);

        Task registrationDisposal = registration.DisposeAsync().AsTask();
        Assert.That(registrationDisposal.IsCompleted, Is.False);
        Task registryDisposal = registry.DisposeAsync().AsTask();
        Assert.That(registryDisposal.IsCompleted, Is.False);

        lease!.Dispose();
        await Task.WhenAll(registrationDisposal, registryDisposal)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task PresentationAndCompletionReplaceIndependentlyOfApplication()
    {
        var original = new TestCapabilities("original");
        var presentationReplacement = new TestCapabilities("presentation");
        var completionReplacement = new TestCapabilities("completion");
        var kind = new AiJobKindId("tests.result");
        await using var registry = new AiJobResultRegistry(
            [new AiJobPresentationRegistration(kind, original)],
            [new AiJobCompletionRegistration(kind, original)],
            [new AiJobResultApplicatorRegistration(kind, original)]);

        IAiJobPresentationRegistration presentationRegistration = registry.Register(
            new AiJobPresentationRegistration(
                kind,
                presentationReplacement,
                AiJobResultRegistrationMode.Replace));
        IAiJobCompletionRegistration completionRegistration = registry.Register(
            new AiJobCompletionRegistration(
                kind,
                completionReplacement,
                AiJobResultRegistrationMode.Replace));
        try
        {
            AssertCapabilities(
                registry,
                kind,
                presentationReplacement,
                completionReplacement,
                original);

            await presentationRegistration.DisposeAsync();
            AssertCapabilities(registry, kind, original, completionReplacement, original);

            await completionRegistration.DisposeAsync();
            AssertCapabilities(registry, kind, original, original, original);
        }
        finally
        {
            await presentationRegistration.DisposeAsync();
            await completionRegistration.DisposeAsync();
        }
    }

    [Test]
    public async Task EachCapabilityHasIndependentCollisionSemantics()
    {
        var kind = new AiJobKindId("tests.independent-slots");
        var capabilities = new TestCapabilities("base");
        await using var registry = new AiJobResultRegistry(
            [new AiJobPresentationRegistration(kind, capabilities)],
            [],
            []);

        Assert.DoesNotThrow(() => registry.Register(
            new AiJobCompletionRegistration(kind, capabilities)));
        Assert.DoesNotThrow(() => registry.Register(
            new AiJobResultApplicatorRegistration(kind, capabilities)));
        Assert.Throws<ArgumentException>(() => registry.Register(
            new AiJobPresentationRegistration(kind, capabilities)));
        Assert.Throws<ArgumentException>(() => registry.Register(
            new AiJobPresentationRegistration(
                new AiJobKindId("tests.missing-slot"),
                capabilities,
                AiJobResultRegistrationMode.Replace)));
    }

    [Test]
    public async Task PresenterExtensionsComposeReplacementAfterLaterBase()
    {
        var extensions = new ExtensionProvider();
        var original = new TestCapabilities("original");
        var replacement = new TestCapabilities("replacement");
        var kind = new AiJobKindId("tests.package-result");
        var replaceExtension = new TestPresentationExtension(
            new AiJobPresentationRegistration(
                kind,
                replacement,
                AiJobResultRegistrationMode.Replace));
        var addExtension = new TestPresentationExtension(
            new AiJobPresentationRegistration(kind, original));
        await using var registry = new AiJobResultRegistry([], [], [], extensions, null);

        extensions.AddExtensions(200, [replaceExtension, addExtension]);
        AssertPresenter(registry, kind, replacement);

        var unrelated = new TestPresentationExtension(
            new AiJobPresentationRegistration(
                new AiJobKindId("tests.unrelated-result"),
                new TestCapabilities("unrelated")));
        extensions.AddExtensions(201, [unrelated]);
        AssertPresenter(registry, kind, replacement);

        await extensions.RemoveExtensions(200).DrainAsync();
        await extensions.RemoveExtensions(201).DrainAsync();
    }

    [Test]
    public async Task RemovingPresenterExtensionActivatesRejectedPeerAndDrainsItsLease()
    {
        var extensions = new ExtensionProvider();
        var failures = new List<AiJobResultExtensionFailure>();
        var outgoing = new TestCapabilities("outgoing");
        var waiting = new TestCapabilities("waiting");
        var kind = new AiJobKindId("tests.handoff-result");
        var outgoingExtension = new TestPresentationExtension(
            new AiJobPresentationRegistration(kind, outgoing));
        var waitingExtension = new TestPresentationExtension(
            new AiJobPresentationRegistration(kind, waiting));
        await using var registry = new AiJobResultRegistry([], [], [], extensions, failures.Add);

        extensions.AddExtensions(203, [outgoingExtension]);
        extensions.AddExtensions(204, [waitingExtension]);
        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(registry.TryAcquirePresenter(kind, out IAiJobPresenterLease? outgoingLease), Is.True);

        Task? removal = null;
        try
        {
            Assert.That(outgoingLease!.Presenter, Is.SameAs(outgoing));
            removal = extensions.RemoveExtensions(203).DrainAsync().AsTask();
            AssertPresenter(registry, kind, waiting);
            Assert.That(removal.IsCompleted, Is.False);
        }
        finally
        {
            outgoingLease?.Dispose();
        }

        await removal!.WaitAsync(TimeSpan.FromSeconds(5));
        await extensions.RemoveExtensions(204).DrainAsync();
    }

    [Test]
    public async Task FailedPresenterExtensionCompositionRollsBackItsProvisionalAdd()
    {
        var extensions = new ExtensionProvider();
        var failures = new List<AiJobResultExtensionFailure>();
        var provisional = new TestCapabilities("provisional");
        var healthy = new TestCapabilities("healthy");
        var kind = new AiJobKindId("tests.invalid-provisional");
        var invalidExtension = new TestPresentationExtension(
            new AiJobPresentationRegistration(kind, provisional),
            new AiJobPresentationRegistration(
                new AiJobKindId("tests.missing-replacement"),
                provisional,
                AiJobResultRegistrationMode.Replace));
        var healthyExtension = new TestPresentationExtension(
            new AiJobPresentationRegistration(kind, healthy));
        await using var registry = new AiJobResultRegistry([], [], [], extensions, failures.Add);

        extensions.AddExtensions(205, [invalidExtension, healthyExtension]);

        AssertPresenter(registry, kind, healthy);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures[0].ExtensionType, Is.EqualTo(invalidExtension.GetType().FullName));
            Assert.That(
                failures[0].Capability,
                Is.EqualTo(AiJobResultCapability.Presentation));
        }

        await extensions.RemoveExtensions(205).DrainAsync();
    }

    [Test]
    public void PresentationAndCompletionExtensionsSupportLiveUnloadButApplicatorsRequireRestart()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new TestPresentationExtension(), Is.InstanceOf<ILiveUnloadExtension>());
            Assert.That(new TestCompletionExtension(), Is.InstanceOf<ILiveUnloadExtension>());
            Assert.That(new TestApplicatorExtension(), Is.Not.InstanceOf<ILiveUnloadExtension>());
        }
    }

    private static void AssertCapabilities(
        AiJobResultRegistry registry,
        AiJobKindId kind,
        IAiJobPresenter presenter,
        IAiJobCompletionPresenter completionPresenter,
        IAiJobResultApplicator applicator)
    {
        Assert.That(registry.TryAcquirePresenter(kind, out IAiJobPresenterLease? presenterLease), Is.True);
        using (presenterLease)
        {
            Assert.That(presenterLease!.Presenter, Is.SameAs(presenter));
        }

        Assert.That(registry.TryAcquireCompletionPresenter(
            kind,
            out IAiJobCompletionPresenterLease? completionLease), Is.True);
        using (completionLease)
        {
            Assert.That(completionLease!.Presenter, Is.SameAs(completionPresenter));
        }

        Assert.That(registry.TryAcquireApplicator(kind, out IAiJobResultApplicatorLease? applicatorLease), Is.True);
        using (applicatorLease)
        {
            Assert.That(applicatorLease!.Applicator, Is.SameAs(applicator));
        }
    }

    private static void AssertPresenter(
        AiJobResultRegistry registry,
        AiJobKindId kind,
        IAiJobPresenter presenter)
    {
        Assert.That(registry.TryAcquirePresenter(kind, out IAiJobPresenterLease? lease), Is.True);
        using (lease)
        {
            Assert.That(lease!.Presenter, Is.SameAs(presenter));
        }
    }

    private sealed class TestCapabilities(string name) :
        IAiJobPresenter,
        IAiJobCompletionPresenter,
        IAiJobResultApplicator
    {
        public AiJobPresentation Present(AiJob job, AiJobStatusSemantics status)
            => new(name, job.Status.Value, name, string.Empty, false);

        public AiJobCompletionPresentation? CreateCompletion(
            AiJob job,
            AiJobStatusSemantics status)
            => new(name, name, AiJobNotificationKind.Information);

        public bool CanApply(AiJob job, AiJobStatusSemantics status) => true;

        public Task ApplyAsync(
            AiJob job,
            IAiJobResultContext context,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class TestPresentationExtension(
        params AiJobPresentationRegistration[] registrations) : AiJobPresentationExtension
    {
        public override IReadOnlyCollection<AiJobPresentationRegistration> Registrations { get; } =
            registrations;
    }

    private sealed class TestCompletionExtension(
        params AiJobCompletionRegistration[] registrations) : AiJobCompletionExtension
    {
        public override IReadOnlyCollection<AiJobCompletionRegistration> Registrations { get; } =
            registrations;
    }

    private sealed class TestApplicatorExtension(
        params AiJobResultApplicatorRegistration[] registrations) : AiJobResultApplicatorExtension
    {
        public override IReadOnlyCollection<AiJobResultApplicatorRegistration> Registrations { get; } =
            registrations;
    }
}

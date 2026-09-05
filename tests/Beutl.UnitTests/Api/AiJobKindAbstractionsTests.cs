using Beutl.Api.Services;
using Beutl.Editor.Services.AI;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class AiJobKindAbstractionsTests
{
    [Test]
    public void PluginContracts_SeparateServerJobKindsFromEditorResultHandling()
    {
        System.Reflection.Assembly contracts = typeof(AiJobKindExtension).Assembly;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contracts.GetName().Name, Is.EqualTo("Beutl.Api"));
            Assert.That(typeof(AiJobKindDescriptor).Assembly, Is.SameAs(contracts));
            Assert.That(typeof(AiJobStatusSemantics).Assembly, Is.SameAs(contracts));
            Assert.That(typeof(AiJob).Assembly, Is.SameAs(contracts));
            Assert.That(typeof(IAiJobResultHandler).Assembly.GetName().Name, Is.EqualTo("Beutl.Editor"));
            Assert.That(typeof(IAiJobResultContext).Assembly, Is.SameAs(typeof(IAiJobResultHandler).Assembly));
            Assert.That(typeof(IAiJobResultEditorContext).Assembly, Is.SameAs(typeof(IAiJobResultHandler).Assembly));
            Assert.That(typeof(AiJobKindRegistry).Assembly, Is.SameAs(contracts));
            Assert.That(contracts.GetType("Beutl.Api.Services.IAiJobResultHandler"), Is.Null);
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
    public async Task EditorResultHandlers_UseExplicitReplacementAndRestoreThePreviousContribution()
    {
        var original = new TestResultHandler();
        var replacement = new TestResultHandler();
        await using var registry = new AiJobResultHandlerRegistry(
        [
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                new AiJobKindId("tests.result"),
                original)),
        ]);

        IAiJobResultHandlerRegistration registration = registry.Register(new AiJobResultHandlerRegistration(
            new AiJobResultContribution(new AiJobKindId("tests.result"), replacement),
            AiJobResultHandlerRegistrationMode.Replace));
        try
        {
            Assert.That(registry.TryAcquire(new AiJobKindId("TESTS.RESULT"), out IAiJobResultHandlerLease? lease), Is.True);
            using (lease)
            {
                Assert.That(lease!.Handler, Is.SameAs(replacement));
            }
        }
        finally
        {
            await registration.DisposeAsync();
        }

        Assert.That(registry.TryAcquire(new AiJobKindId("tests.result"), out IAiJobResultHandlerLease? restoredLease), Is.True);
        using (restoredLease)
        {
            Assert.That(restoredLease!.Handler, Is.SameAs(original));
        }
    }

    [Test]
    public async Task EditorResultHandlerExtensionsComposeReplacementAfterLaterBase()
    {
        var extensions = new ExtensionProvider();
        var original = new TestResultHandler();
        var replacement = new TestResultHandler();
        var replaceExtension = new TestResultHandlerExtension(
            new AiJobResultHandlerRegistration(
                new AiJobResultContribution(new AiJobKindId("tests.package-result"), replacement),
                AiJobResultHandlerRegistrationMode.Replace));
        var addExtension = new TestResultHandlerExtension(
            new AiJobResultHandlerRegistration(
                new AiJobResultContribution(new AiJobKindId("tests.package-result"), original)));
        await using var registry = new AiJobResultHandlerRegistry([], extensions);

        extensions.AddExtensions(200, [replaceExtension, addExtension]);

        Assert.That(registry.TryAcquire(
            new AiJobKindId("tests.package-result"),
            out IAiJobResultHandlerLease? lease), Is.True);
        using (lease)
        {
            Assert.That(lease!.Handler, Is.SameAs(replacement));
        }

        // A later unrelated extension must recompose from the currently effective replacement,
        // not replay its raw Replace operation before the base that arrived afterward.
        var unrelated = new TestResultHandlerExtension(
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                new AiJobKindId("tests.unrelated-result"),
                new TestResultHandler())));
        extensions.AddExtensions(201, [unrelated]);

        Assert.That(registry.TryAcquire(
            new AiJobKindId("tests.package-result"),
            out IAiJobResultHandlerLease? recomposedLease), Is.True);
        using (recomposedLease)
        {
            Assert.That(recomposedLease!.Handler, Is.SameAs(replacement));
        }

        await extensions.RemoveExtensions(200).DrainAsync();
        await extensions.RemoveExtensions(201).DrainAsync();
    }

    [Test]
    public async Task EffectiveReplacementSurvivesBaseDisposeDuringUnrelatedExtensionComposition()
    {
        var extensions = new ExtensionProvider();
        var original = new TestResultHandler();
        var replacement = new TestResultHandler();
        await using var registry = new AiJobResultHandlerRegistry([], extensions);
        IAiJobResultHandlerRegistration hostBase = registry.Register(new AiJobResultHandlerRegistration(
            new AiJobResultContribution(new AiJobKindId("tests.host-result"), original)));
        IAiJobResultHandlerRegistration hostReplacement = registry.Register(new AiJobResultHandlerRegistration(
            new AiJobResultContribution(new AiJobKindId("tests.host-result"), replacement),
            AiJobResultHandlerRegistrationMode.Replace));

        await hostBase.DisposeAsync();
        Assert.That(registry.TryAcquire(
            new AiJobKindId("tests.host-result"),
            out IAiJobResultHandlerLease? liveLease), Is.True);
        using (liveLease)
        {
            Assert.That(liveLease!.Handler, Is.SameAs(replacement));
        }

        var unrelated = new TestResultHandlerExtension(
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                new AiJobKindId("tests.unrelated-host-result"),
                new TestResultHandler())));
        extensions.AddExtensions(202, [unrelated]);

        Assert.That(registry.TryAcquire(
            new AiJobKindId("tests.unrelated-host-result"),
            out IAiJobResultHandlerLease? unrelatedLease), Is.True);
        unrelatedLease!.Dispose();
        Assert.That(registry.TryAcquire(
            new AiJobKindId("tests.host-result"),
            out IAiJobResultHandlerLease? replacementLease), Is.True);
        using (replacementLease)
        {
            Assert.That(replacementLease!.Handler, Is.SameAs(replacement));
        }
        await extensions.RemoveExtensions(202).DrainAsync();
        await hostReplacement.DisposeAsync();
    }

    [Test]
    public async Task RemovingAnExtensionLetsARejectedSameKindExtensionActivateImmediately()
    {
        var extensions = new ExtensionProvider();
        var failures = new List<AiJobResultHandlerExtensionFailure>();
        var outgoing = new TestResultHandler();
        var waiting = new TestResultHandler();
        var outgoingExtension = new TestResultHandlerExtension(
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                new AiJobKindId("tests.handoff-result"),
                outgoing)));
        var waitingExtension = new TestResultHandlerExtension(
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                new AiJobKindId("tests.handoff-result"),
                waiting)));
        await using var registry = new AiJobResultHandlerRegistry([], extensions, failures.Add);

        extensions.AddExtensions(203, [outgoingExtension]);
        extensions.AddExtensions(204, [waitingExtension]);
        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(registry.TryAcquire(
            new AiJobKindId("tests.handoff-result"),
            out IAiJobResultHandlerLease? outgoingLease), Is.True);

        Task? removal = null;
        try
        {
            Assert.That(outgoingLease!.Handler, Is.SameAs(outgoing));
            removal = extensions.RemoveExtensions(203).DrainAsync().AsTask();
            Assert.That(registry.TryAcquire(
                new AiJobKindId("tests.handoff-result"),
                out IAiJobResultHandlerLease? waitingLease), Is.True);
            using (waitingLease)
            {
                Assert.That(waitingLease!.Handler, Is.SameAs(waiting));
            }
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
    public async Task FailedExtensionCompositionRollsBackProvisionalAddBeforeReevaluatingLaterAdd()
    {
        var extensions = new ExtensionProvider();
        var failures = new List<AiJobResultHandlerExtensionFailure>();
        var provisional = new TestResultHandler();
        var healthy = new TestResultHandler();
        var invalidExtension = new TestResultHandlerExtension(
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                new AiJobKindId("tests.invalid-provisional"),
                provisional)),
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                new AiJobKindId("tests.missing-replacement"),
                provisional),
                AiJobResultHandlerRegistrationMode.Replace));
        var healthyExtension = new TestResultHandlerExtension(
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                new AiJobKindId("tests.invalid-provisional"),
                healthy)));

        await using var registry = new AiJobResultHandlerRegistry(
            [],
            extensions,
            failures.Add);

        extensions.AddExtensions(201, [invalidExtension, healthyExtension]);

        Assert.That(registry.TryAcquire(
            new AiJobKindId("tests.invalid-provisional"),
            out IAiJobResultHandlerLease? lease), Is.True);
        Task? drain = null;
        try
        {
            Assert.That(lease!.Handler, Is.SameAs(healthy));
            Assert.Multiple(() =>
            {
                Assert.That(failures, Has.Count.EqualTo(1));
                Assert.That(failures[0].ExtensionType, Is.EqualTo(invalidExtension.GetType().FullName));
            });

            drain = extensions.RemoveExtensions(201).DrainAsync().AsTask();
            Assert.That(drain.IsCompleted, Is.False);
        }
        finally
        {
            lease?.Dispose();
        }
        await drain!.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class TestResultHandler : IAiJobResultHandler
    {
        public AiJobPresentation Present(AiJob job, AiJobStatusSemantics status)
            => new("Test", job.Status.Value, "Test", string.Empty, false);

        public AiJobCompletionPresentation? CreateCompletion(
            AiJob job,
            AiJobStatusSemantics status,
            AiJobPresentation presentation)
            => null;

        public bool CanHandle(AiJob job, AiJobStatusSemantics status) => true;

        public Task HandleAsync(
            AiJob job,
            IAiJobResultContext context,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class TestResultHandlerExtension(
        params AiJobResultHandlerRegistration[] registrations) : AiJobResultHandlerExtension
    {
        public override IReadOnlyCollection<AiJobResultHandlerRegistration> Registrations { get; } =
            registrations;
    }
}

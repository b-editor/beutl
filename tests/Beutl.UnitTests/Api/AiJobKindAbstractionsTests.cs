using Beutl.Api.Services;
using Beutl.Editor.Services.AI;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class AiJobKindAbstractionsTests
{
    [Test]
    public void PluginContracts_LiveInMinimalAbstractionsAssembly()
    {
        System.Reflection.Assembly contracts = typeof(AiJobKindExtension).Assembly;
        string[] dependencies = contracts.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contracts.GetName().Name, Is.EqualTo("Beutl.Api.Abstractions"));
            Assert.That(typeof(AiJobKindDescriptor).Assembly, Is.SameAs(contracts));
            Assert.That(typeof(AiJobStatusSemantics).Assembly, Is.SameAs(contracts));
            Assert.That(typeof(AiJob).Assembly, Is.SameAs(contracts));
            Assert.That(typeof(IAiJobResultHandler).Assembly.GetName().Name, Is.EqualTo("Beutl.Editor.Abstractions"));
            Assert.That(typeof(IAiJobResultContext).Assembly, Is.SameAs(typeof(IAiJobResultHandler).Assembly));
            Assert.That(typeof(AiJobKindRegistry).Assembly, Is.Not.SameAs(contracts));
            Assert.That(dependencies, Does.Not.Contain("Beutl.Api"));
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
    public void EditorResultHandlers_UseExplicitReplacementAndRestoreThePreviousContribution()
    {
        var original = new TestResultHandler();
        var replacement = new TestResultHandler();
        using var registry = new AiJobResultHandlerRegistry(
        [
            new AiJobResultHandlerRegistration(new AiJobResultContribution(
                new AiJobKindId("tests.result"),
                original)),
        ]);

        IDisposable registration = registry.Register(new AiJobResultHandlerRegistration(
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
            registration.Dispose();
        }

        Assert.That(registry.TryAcquire(new AiJobKindId("tests.result"), out IAiJobResultHandlerLease? restoredLease), Is.True);
        using (restoredLease)
        {
            Assert.That(restoredLease!.Handler, Is.SameAs(original));
        }
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
}

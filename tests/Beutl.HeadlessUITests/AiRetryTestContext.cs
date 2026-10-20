using Beutl.Api.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

internal static class AiRetryTestContext
{
    public static AiRequestRecoveryContext CreateForm()
        => new(
            new FileAiRequestKeyMap(TestDirectory("ai-request-recovery")),
            () => new AiAuthenticatedRequestIdentity("test-user", User: null));

    // A new folder under the test home, which the assembly's teardown deletes; the map creates it.
    private static string TestDirectory(string name)
        => Path.Combine(BeutlHomeIsolation.CurrentHome!, name, Guid.NewGuid().ToString("N"));

    // The built-in job kinds over a video service that the tests never call.
    public static AiJobKindRegistry CreateBuiltInJobKinds()
        => AiJobKindRegistry.CreateBuiltIn(new UnusedVideoService());

    private sealed class UnusedVideoService : IAiVideoService
    {
        public Task<AiVideoGenerationResult> CreateFromSourceAsync(
            AiSourceVideoRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<AiVideoGenerationResult> CreateAsync(
            AiVideoGenerationRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<AiVideoJob> GetAsync(AiJobId jobId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}

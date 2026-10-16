using Beutl.Api.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

internal static class AiRetryTestContext
{
    public static AiRetryAttemptContext Create()
        => new(
            new FileAiRetryKeyStore(TestDirectory("retry-keys")),
            () => new AiAuthenticatedRequestIdentity("headless-account", User: null),
            allowSyntheticIdentity: true);

    public static AiRequestRecoveryContext CreateForm()
        => new(
            new FileAiRequestRecoveryStore(TestDirectory("ai-request-recovery")),
            () => new AiAuthenticatedRequestIdentity("test-user", User: null));

    // A new folder under the test home, which the assembly's teardown deletes; the stores create it.
    private static string TestDirectory(string name)
        => Path.Combine(BeutlHomeIsolation.CurrentHome!, name, Guid.NewGuid().ToString("N"));

    // The built-in job kinds over generation services that the tests never call.
    public static AiJobKindRegistry CreateBuiltInJobKinds()
        => AiJobKindRegistry.CreateBuiltIn(
            new UnusedImageGenerationService(),
            new UnusedVideoService(),
            new UnusedEntitlementService(),
            new UnusedAvailabilityService(),
            new UnusedModelCatalogService(),
            Create());

    private sealed class UnusedImageGenerationService : IAiImageGenerationService
    {
        public Task<AiImageResult> GenerateAsync(
            AiImageGenerationRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<AiImageResult> GenerateAsync(
            AiImageGenerationRequest request,
            IProgress<AiImagePreview>? progress,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

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

    private sealed class UnusedEntitlementService : IAiEntitlementService
    {
        public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements { get; } =
            new ReactivePropertySlim<AiEntitlements?>();

        public Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class UnusedAvailabilityService : IAiOperationAvailabilityService
    {
        public Task<bool> CheckAsync(
            AiOperationAvailabilityRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class UnusedModelCatalogService : IAiModelCatalogService
    {
        public Task<AiModelCatalog> GetAsync(CancellationToken cancellationToken)
            => Task.FromResult(AiModelCatalog.Empty);

        public void Invalidate()
        {
        }
    }
}

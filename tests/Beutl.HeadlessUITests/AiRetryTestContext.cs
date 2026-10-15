using Beutl.Api.Services;
using Beutl.Services.AI;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

internal static class AiRetryTestContext
{
    public static AiRetryAttemptContext Create()
        => new(
            new FileAiRetryKeyStore(Path.Combine(
                Path.GetTempPath(),
                "Beutl.HeadlessUITests",
                "retry-keys",
                Guid.NewGuid().ToString("N"))),
            () => new AiAuthenticatedRequestIdentity("headless-account", User: null),
            allowSyntheticIdentity: true);

    public static AiRequestRecoveryContext CreateForm()
        => new(
            new FileAiRequestRecoveryStore(Path.Combine(
                Path.GetTempPath(),
                "Beutl.HeadlessUITests",
                "ai-request-recovery",
                Guid.NewGuid().ToString("N"))),
            () => new AiAuthenticatedRequestIdentity("test-user", User: null));

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

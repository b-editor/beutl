namespace Beutl.Api.Services;

internal sealed record BuiltInAiJobBehaviorRegistrations(
    IReadOnlyList<AiJobStatusResolverRegistration> StatusResolvers,
    IReadOnlyList<AiJobRefreshHandlerRegistration> RefreshHandlers,
    IReadOnlyList<AiJobRetryHandlerRegistration> RetryHandlers);

internal static class BuiltInAiJobKinds
{
    public static BuiltInAiJobBehaviorRegistrations Create(
        IAiImageGenerationService images,
        IAiVideoService videos,
        IAiEntitlementService entitlements,
        IAiOperationAvailabilityService availability,
        IAiModelCatalogService models,
        AiRetryAttemptContext retryContext)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(videos);
        ArgumentNullException.ThrowIfNull(entitlements);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(retryContext);

        var statuses = new AiJobStatusMap(
        [
            KeyValuePair.Create(AiJobStatuses.Queued, new AiJobStatusSemantics(false, true)),
            KeyValuePair.Create(AiJobStatuses.Running, new AiJobStatusSemantics(false, true)),
            KeyValuePair.Create(AiJobStatuses.Finalizing, new AiJobStatusSemantics(false, true)),
            KeyValuePair.Create(
                AiJobStatuses.Succeeded,
                new AiJobStatusSemantics(true, false, AiJobOutcomes.Succeeded)),
            KeyValuePair.Create(
                AiJobStatuses.Failed,
                new AiJobStatusSemantics(true, false, AiJobOutcomes.Failed)),
            KeyValuePair.Create(
                AiJobStatuses.Canceled,
                new AiJobStatusSemantics(true, false, AiJobOutcomes.Canceled)),
        ]);
        AiJobKindId[] kinds =
        [
            AiJobKinds.Image,
            AiJobKinds.ImageEdit,
            AiJobKinds.Transcription,
            AiJobKinds.CaptionTranslation,
            AiJobKinds.Video,
        ];
        return new BuiltInAiJobBehaviorRegistrations(
            kinds.Select(kind => new AiJobStatusResolverRegistration(kind, statuses)).ToArray(),
            [
                new AiJobRefreshHandlerRegistration(
                    AiJobKinds.Video,
                    new AiVideoJobRefreshHandler(videos)),
            ],
            [
                new AiJobRetryHandlerRegistration(
                    AiJobKinds.Image,
                    new AiImageJobRetryHandler(
                        images,
                        entitlements,
                        availability,
                        models,
                        retryContext)),
                new AiJobRetryHandlerRegistration(
                    AiJobKinds.Video,
                    new AiVideoJobRetryHandler(
                        videos,
                        entitlements,
                        availability,
                        models,
                        retryContext)),
            ]);
    }
}

internal sealed class AiVideoJobRefreshHandler(IAiVideoService videos) : IAiJobRefreshHandler
{
    public async Task RefreshAsync(AiJob job, CancellationToken cancellationToken)
        => await videos.GetAsync(job.Id, cancellationToken);
}

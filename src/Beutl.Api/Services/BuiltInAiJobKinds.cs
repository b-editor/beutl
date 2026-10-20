namespace Beutl.Api.Services;

internal static class BuiltInAiJobKinds
{
    public static AiJobKindRegistry Create(
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
        return new AiJobKindRegistry(
            kinds.Select(kind => KeyValuePair.Create(kind, (IAiJobStatusResolver)statuses)),
            [KeyValuePair.Create(AiJobKinds.Video, (IAiJobRefreshHandler)new AiVideoJobRefreshHandler(videos))],
            [
                KeyValuePair.Create(
                    AiJobKinds.Image,
                    (IAiJobRetryHandler)new AiImageJobRetryHandler(
                        images,
                        entitlements,
                        availability,
                        models,
                        retryContext)),
                KeyValuePair.Create(
                    AiJobKinds.Video,
                    (IAiJobRetryHandler)new AiVideoJobRetryHandler(
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

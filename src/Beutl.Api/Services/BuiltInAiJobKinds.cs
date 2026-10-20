namespace Beutl.Api.Services;

internal static class BuiltInAiJobKinds
{
    public static AiJobKindRegistry Create(IAiVideoService videos)
    {
        ArgumentNullException.ThrowIfNull(videos);

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
            [KeyValuePair.Create(AiJobKinds.Video, (IAiJobRefreshHandler)new AiVideoJobRefreshHandler(videos))]);
    }
}

internal sealed class AiVideoJobRefreshHandler(IAiVideoService videos) : IAiJobRefreshHandler
{
    public async Task RefreshAsync(AiJob job, CancellationToken cancellationToken)
        => await videos.GetAsync(job.Id, cancellationToken);
}

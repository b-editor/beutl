using System.Text.Json;

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

internal static class AiJobInputParameters
{
    public static string? GetString(AiJob job, string propertyName)
    {
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return NormalizeText(value.GetString());
    }

    public static int? GetInt32(AiJob job, string propertyName)
    {
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int result))
        {
            return null;
        }

        return result;
    }

    public static bool? GetBoolean(AiJob job, string propertyName)
    {
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    public static bool Has(AiJob job, string propertyName)
        => job.InputParameters is { ValueKind: JsonValueKind.Object } input
           && input.TryGetProperty(propertyName, out JsonElement value)
           && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class AiVideoJobRefreshHandler(IAiVideoService videos) : IAiJobRefreshHandler
{
    public async Task RefreshAsync(AiJob job, CancellationToken cancellationToken)
        => await videos.GetAsync(job.Id, cancellationToken);
}

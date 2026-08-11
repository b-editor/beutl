using System.Text.Json;
using Beutl.Language;

namespace Beutl.Api.Services;

internal static class BuiltInAiJobKinds
{
    public static IReadOnlyList<AiJobKindDescriptor> Create(
        IAiImageGenerationService images,
        IAiVideoService videos,
        IAiEntitlementService entitlements)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(videos);
        ArgumentNullException.ThrowIfNull(entitlements);

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
        return
        [
            new AiJobKindDescriptor(
                AiJobKinds.Image,
                statuses)
            {
                RetryHandler = new AiImageJobRetryHandler(images, entitlements),
            },
            new AiJobKindDescriptor(
                AiJobKinds.ImageEdit,
                statuses),
            new AiJobKindDescriptor(
                AiJobKinds.Transcription,
                statuses),
            new AiJobKindDescriptor(
                AiJobKinds.CaptionTranslation,
                statuses),
            new AiJobKindDescriptor(
                AiJobKinds.Video,
                statuses)
            {
                RefreshHandler = new AiVideoJobRefreshHandler(videos),
                RetryHandler = new AiVideoJobRetryHandler(videos, entitlements),
            },
        ];
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

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal abstract class MeteredAiJobRetryHandler(
    AiOperationId operation,
    IAiEntitlementService entitlementService) : IAiJobRetryHandler
{
    public bool CanRetry(AiJob job, AiJobStatusSemantics status)
        => status.Outcome == AiJobOutcomes.Failed
            && job.CanRetry
            && AiJobInputParameters.GetString(job, "prompt") is not null;

    public async ValueTask<AiJobRetryPreflight> GetPreflightAsync(
        AiJob job,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AiEntitlements? entitlements = await entitlementService.RefreshAsync(cancellationToken);
        AiJobRetryPreflight result;
        if (entitlements is null)
        {
            result = new AiJobRetryPreflight(false, false, Strings.AiPricingUnavailable);
        }
        else if (!entitlements.CanUseAi)
        {
            result = new AiJobRetryPreflight(true, false, Strings.AiProRequired);
        }
        else if (!entitlements.Availability.CanStart(operation))
        {
            result = new AiJobRetryPreflight(true, false, Strings.AiEstimatedUsageInsufficient);
        }
        else
        {
            string explanation = entitlements.Balance.MonthlyUsage.IsExhausted
                ? Strings.AiEstimatedUsageTopUp
                : Strings.AiEstimatedUsageMonthly;
            result = new AiJobRetryPreflight(true, true, explanation);
        }

        return result;
    }

    public abstract Task RetryAsync(
        AiJob job,
        CancellationToken cancellationToken);
}

internal sealed class AiImageJobRetryHandler(
    IAiImageGenerationService images,
    IAiEntitlementService entitlementService)
    : MeteredAiJobRetryHandler(AiOperations.ImageGeneration, entitlementService)
{
    public override Task RetryAsync(
        AiJob job,
        CancellationToken cancellationToken)
    {
        string prompt = AiJobInputParameters.GetString(job, "prompt")
            ?? throw new InvalidOperationException("The retained image prompt is missing.");
        string? imageSize = AiJobInputParameters.GetString(job, "size");
        string size = imageSize is "1024x1024" or "1024x1536" or "1536x1024"
            ? imageSize
            : "1024x1024";
        return images.GenerateAsync(
            new AiImageGenerationRequest(prompt, new AiImageSizeId(size)),
            cancellationToken);
    }
}

internal sealed class AiVideoJobRetryHandler(
    IAiVideoService videos,
    IAiEntitlementService entitlementService)
    : MeteredAiJobRetryHandler(AiOperations.VideoGeneration, entitlementService)
{
    public override Task RetryAsync(
        AiJob job,
        CancellationToken cancellationToken)
    {
        string prompt = AiJobInputParameters.GetString(job, "prompt")
            ?? throw new InvalidOperationException("The retained video prompt is missing.");
        int? durationSeconds = AiJobInputParameters.GetInt32(job, "durationSeconds");
        string? resolution = AiJobInputParameters.GetString(job, "resolution");
        return videos.CreateAsync(
            new AiVideoGenerationRequest(
                prompt,
                durationSeconds is 4 or 6 or 8 ? durationSeconds.Value : 6,
                new AiVideoResolutionId(resolution is "720p" or "1080p" ? resolution : "720p")),
            cancellationToken);
    }
}

internal sealed class AiVideoJobRefreshHandler(IAiVideoService videos) : IAiJobRefreshHandler
{
    public async Task RefreshAsync(AiJob job, CancellationToken cancellationToken)
        => await videos.GetAsync(job.Id, cancellationToken);
}

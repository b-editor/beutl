using System.Collections.Immutable;
using System.Text.Json;
using Beutl.Language;

namespace Beutl.Api.Services;

internal static class BuiltInAiJobKinds
{
    public static IReadOnlyList<AiJobKindDescriptor> Create(
        IAiImageGenerationService images,
        IAiVideoService videos,
        IAiEntitlementService entitlements,
        IAiOperationAvailabilityService availability,
        IAiModelCatalogService models)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(videos);
        ArgumentNullException.ThrowIfNull(entitlements);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(models);

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
                RetryHandler = new AiImageJobRetryHandler(images, entitlements, availability, models),
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
                RetryHandler = new AiVideoJobRetryHandler(videos, entitlements, availability, models),
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

internal abstract class MeteredAiJobRetryHandler(
    AiOperationId operation,
    IAiEntitlementService entitlementService,
    IAiOperationAvailabilityService availabilityService,
    IAiModelCatalogService modelCatalogService) : IAiJobRetryHandler
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
        else if (!await IsModelStillOfferedAsync(job, cancellationToken))
        {
            // The balance is not the problem, so saying it is would send the
            // user to buy credits that would not help.
            result = new AiJobRetryPreflight(true, false, Strings.AiModelUnavailable);
        }
        else if (entitlements.Availability.GetState(operation) == AiOperationAvailabilityState.Unavailable
                 || !await availabilityService.CheckAsync(
                     CreateAvailabilityRequest(job),
                     cancellationToken))
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

    protected abstract AiOperationAvailabilityRequest CreateAvailabilityRequest(AiJob job);

    protected async Task EnsureAvailableAsync(
        AiJob job,
        CancellationToken cancellationToken)
    {
        if (!await IsModelStillOfferedAsync(job, cancellationToken))
            throw new AiModelUnavailableException();
        if (!await availabilityService.CheckAsync(
                CreateAvailabilityRequest(job),
                cancellationToken))
        {
            throw new AiUsageLimitExceededException();
        }
    }

    // A rerun repeats the model the job ran on, so a model that has since been
    // withdrawn cannot be repeated. Falling back to the operation's default
    // would quietly produce something else and charge the default's price for
    // it; the server refuses this too.
    private async Task<bool> IsModelStillOfferedAsync(
        AiJob job,
        CancellationToken cancellationToken)
    {
        if (job.Model is not { Value.Length: > 0 } model)
            return true;

        AiModelCatalog catalog = await modelCatalogService.GetAsync(cancellationToken);
        ImmutableArray<AiModelOption> models = catalog.ModelsFor(operation);
        // A catalog that could not be fetched says nothing about any model, and
        // the server has the last word regardless.
        if (models.IsDefaultOrEmpty)
            return true;

        return models.Any(option => option.Id == model);
    }
}

internal sealed class AiImageJobRetryHandler(
    IAiImageGenerationService images,
    IAiEntitlementService entitlementService,
    IAiOperationAvailabilityService availabilityService,
    IAiModelCatalogService modelCatalogService)
    : MeteredAiJobRetryHandler(
        AiOperations.ImageGeneration,
        entitlementService,
        availabilityService,
        modelCatalogService)
{
    protected override AiOperationAvailabilityRequest CreateAvailabilityRequest(AiJob job)
        => new AiOperationAvailabilityRequest.Fixed(AiOperations.ImageGeneration, job.Model);

    public override async Task RetryAsync(
        AiJob job,
        CancellationToken cancellationToken)
    {
        string prompt = AiJobInputParameters.GetString(job, "prompt")
            ?? throw new InvalidOperationException("The retained image prompt is missing.");
        // A generation guided by a reference image cannot be repeated: the
        // picture itself was never retained, so this would produce something
        // else at full price. The server refuses it too.
        if (AiJobInputParameters.Has(job, "reference"))
        {
            throw new InvalidOperationException(
                "An image generated from a reference image cannot be retried.");
        }

        await EnsureAvailableAsync(job, cancellationToken);
        await images.GenerateAsync(
            new AiImageGenerationRequest(
                prompt,
                new AiImageAspectRatioId(ResolveAspectRatio(job)),
                // Rerun with whatever background the run recorded. A job that
                // recorded none asked the model to decide, which is what an
                // empty id sends.
                background: ResolveBackground(job),
                seed: AiJobInputParameters.GetInt32(job, "seed"),
                model: job.Model),
            cancellationToken);
    }

    private static AiImageBackgroundId ResolveBackground(AiJob job)
    {
        string? background = AiJobInputParameters.GetString(job, "background");
        return string.IsNullOrWhiteSpace(background)
            ? default
            : new AiImageBackgroundId(background);
    }

    // Jobs recorded before the endpoint spoke ratios carry the fixed size they
    // were asked for. Mapping it back is what keeps a repeat the same shape.
    private static string ResolveAspectRatio(AiJob job)
    {
        string? aspectRatio = AiJobInputParameters.GetString(job, "aspectRatio");
        if (aspectRatio is not null)
            return aspectRatio;

        return AiJobInputParameters.GetString(job, "size") switch
        {
            "1024x1536" => "2:3",
            "1536x1024" => "3:2",
            _ => "1:1",
        };
    }
}

internal sealed class AiVideoJobRetryHandler(
    IAiVideoService videos,
    IAiEntitlementService entitlementService,
    IAiOperationAvailabilityService availabilityService,
    IAiModelCatalogService modelCatalogService)
    : MeteredAiJobRetryHandler(
        AiOperations.VideoGeneration,
        entitlementService,
        availabilityService,
        modelCatalogService)
{
    protected override AiOperationAvailabilityRequest CreateAvailabilityRequest(AiJob job)
        => new AiOperationAvailabilityRequest.Video(GetDurationSeconds(job), job.Model);

    public override async Task RetryAsync(
        AiJob job,
        CancellationToken cancellationToken)
    {
        string prompt = AiJobInputParameters.GetString(job, "prompt")
            ?? throw new InvalidOperationException("The retained video prompt is missing.");
        // Same rule as a reference image: the frames were not retained, so a
        // repeat would be a different video charged at the same price.
        if (AiJobInputParameters.Has(job, "firstFrame")
            || AiJobInputParameters.Has(job, "lastFrame"))
        {
            throw new InvalidOperationException(
                "A video generated from source frames cannot be retried.");
        }

        int durationSeconds = GetDurationSeconds(job);
        string? resolution = AiJobInputParameters.GetString(job, "resolution");
        string? aspectRatio = AiJobInputParameters.GetString(job, "aspectRatio");
        await EnsureAvailableAsync(job, cancellationToken);
        await videos.CreateAsync(
            new AiVideoGenerationRequest(
                prompt,
                durationSeconds,
                new AiVideoResolutionId(resolution is "720p" or "1080p" ? resolution : "720p"),
                // Both defaults match what the endpoint applies to a request
                // that omits them, so a job recorded before they existed is
                // repeated exactly as it ran.
                new AiVideoAspectRatioId(aspectRatio is "16:9" or "9:16" ? aspectRatio : "16:9"),
                generateAudio: AiJobInputParameters.GetBoolean(job, "generateAudio") ?? true,
                seed: AiJobInputParameters.GetInt32(job, "seed"),
                model: job.Model),
            cancellationToken);
    }

    // The length the job ran at, so a rerun repeats the clip that was asked
    // for. Only a length the server would refuse falls back, and any whole
    // second in range is one some model takes.
    private static int GetDurationSeconds(AiJob job)
    {
        int? durationSeconds = AiJobInputParameters.GetInt32(job, "durationSeconds");
        return durationSeconds is { } seconds
               && seconds >= AiRequestLimits.MinVideoDurationSeconds
               && seconds <= AiRequestLimits.MaxVideoDurationSeconds
            ? seconds
            : 6;
    }
}

internal sealed class AiVideoJobRefreshHandler(IAiVideoService videos) : IAiJobRefreshHandler
{
    public async Task RefreshAsync(AiJob job, CancellationToken cancellationToken)
        => await videos.GetAsync(job.Id, cancellationToken);
}

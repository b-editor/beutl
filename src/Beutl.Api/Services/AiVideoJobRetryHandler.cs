using System.Collections.Immutable;
using System.Text.Json;

namespace Beutl.Api.Services;

internal sealed class AiVideoJobRetryHandler(
    IAiVideoService videos,
    IAiEntitlementService entitlementService,
    IAiOperationAvailabilityService availabilityService,
    IAiModelCatalogService modelCatalogService,
    AiRetryAttemptContext retryContext)
    : MeteredAiJobRetryHandler(
        AiOperations.VideoGeneration,
        entitlementService,
        availabilityService,
        modelCatalogService,
        retryContext)
{
    private static readonly HashSet<string> s_allowedProperties =
        ["prompt", "durationSeconds", "resolution", "aspectRatio", "generateAudio", "seed"];

    public override bool CanRetry(AiJob job, AiJobStatusSemantics status)
        => base.CanRetry(job, status)
            && TryParseReplayInput(job, out _);

    protected override AiOperationAvailabilityRequest CreateAvailabilityRequest(AiJob job)
        => new AiOperationAvailabilityRequest.Video(
            AiOperations.VideoGeneration,
            ParseReplayInput(job).DurationSeconds,
            job.Model);

    protected override void ValidateRetryInputs(AiJob job)
    {
        _ = ParseReplayInput(job);
    }

    protected override bool IsCurrentRequestSupported(AiModelCatalog catalog, AiJob job)
    {
        if (!TryParseReplayInput(job, out VideoReplayInput input))
            return false;

        AiModelOption? model = ResolveModel(catalog, job);
        AiVideoModelCapabilities? capabilities = model?.Video;
        if (capabilities is null)
            return true;
        return (!capabilities.DurationsSeconds.IsSpecified
                || capabilities.DurationsSeconds.Values.Contains(input.DurationSeconds))
            && (!capabilities.Resolutions.IsSpecified
                || capabilities.Resolutions.Values.Contains(input.Resolution))
            && (!capabilities.AspectRatios.IsSpecified
                || capabilities.AspectRatios.Values.Contains(input.AspectRatio))
            && (!input.GenerateAudio || capabilities.SupportsAudio)
            && (input.Seed is null || capabilities.SupportsSeed);
    }

    protected override async Task DispatchAsync(
        AiJob job,
        string idempotencyKey,
        bool isRepeat)
    {
        VideoReplayInput input = ParseReplayInput(job);
        await videos.CreateAsync(
            new AiVideoGenerationRequest(
                input.Prompt,
                input.DurationSeconds,
                new AiVideoResolutionId(input.Resolution),
                new AiVideoAspectRatioId(input.AspectRatio),
                generateAudio: input.GenerateAudio,
                seed: input.Seed,
                model: job.Model,
                idempotencyKey: idempotencyKey),
            CancellationToken.None);
    }

    private static VideoReplayInput ParseReplayInput(AiJob job)
    {
        if (!TryParseReplayInput(job, out VideoReplayInput input))
            throw new InvalidOperationException("The retained video request cannot be replayed exactly.");
        return input;
    }

    private static bool TryParseReplayInput(AiJob job, out VideoReplayInput result)
    {
        result = default;
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } input)
            return false;
        foreach (JsonProperty property in input.EnumerateObject())
        {
            if (!s_allowedProperties.Contains(property.Name))
                return false;
        }
        if (!input.TryGetProperty("prompt", out JsonElement prompt)
            || prompt.ValueKind != JsonValueKind.String
            || prompt.GetString() is not { } promptValue
            || string.IsNullOrWhiteSpace(promptValue)
            || promptValue.Length > AiRequestLimits.MaxPromptLength
            || !string.Equals(promptValue, promptValue.Trim(), StringComparison.Ordinal))
            return false;

        int durationSeconds = 4;
        if (input.TryGetProperty("durationSeconds", out JsonElement duration))
        {
            if (duration.ValueKind != JsonValueKind.Number
                || !duration.TryGetInt32(out durationSeconds)
                || durationSeconds < AiRequestLimits.MinVideoDurationSeconds
                || durationSeconds > AiRequestLimits.MaxVideoDurationSeconds)
                return false;
        }

        string resolution = "720p";
        if (input.TryGetProperty("resolution", out JsonElement resolutionElement))
        {
            string? parsedResolution = resolutionElement.ValueKind == JsonValueKind.String
                ? resolutionElement.GetString()
                : null;
            if (parsedResolution is not ("480p" or "720p" or "1080p" or "2K"))
                return false;
            resolution = parsedResolution;
        }

        string aspectRatio = "16:9";
        if (input.TryGetProperty("aspectRatio", out JsonElement aspectElement))
        {
            string? parsedAspect = aspectElement.ValueKind == JsonValueKind.String
                ? aspectElement.GetString()
                : null;
            if (!AiReplayInputValidation.IsStructurallyValidAspectRatio(parsedAspect))
                return false;
            aspectRatio = parsedAspect;
        }

        bool generateAudio = true;
        if (input.TryGetProperty("generateAudio", out JsonElement audio))
        {
            if (audio.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;
            generateAudio = audio.GetBoolean();
        }

        int? seed = null;
        if (input.TryGetProperty("seed", out JsonElement seedElement))
        {
            if (seedElement.ValueKind != JsonValueKind.Number
                || !seedElement.TryGetInt32(out int seedValue)
                || seedValue < AiRequestLimits.MinSeed
                || seedValue > AiRequestLimits.MaxSeed)
                return false;
            seed = seedValue;
        }

        result = new VideoReplayInput(
            promptValue,
            durationSeconds,
            resolution,
            aspectRatio,
            generateAudio,
            seed);
        return true;
    }

    private static AiModelOption? ResolveModel(AiModelCatalog catalog, AiJob job)
    {
        ImmutableArray<AiModelOption> models = catalog.ModelsFor(AiOperations.VideoGeneration);
        if (models.IsDefaultOrEmpty)
            return null;
        if (job.Model is { Value.Length: > 0 } model)
            return models.FirstOrDefault(option => option.Id == model);
        return catalog.DefaultFor(AiOperations.VideoGeneration);
    }

    private readonly record struct VideoReplayInput(
        string Prompt,
        int DurationSeconds,
        string Resolution,
        string AspectRatio,
        bool GenerateAudio,
        int? Seed);
}

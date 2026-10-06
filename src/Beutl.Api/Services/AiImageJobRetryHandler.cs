using System.Collections.Immutable;
using System.Text.Json;

namespace Beutl.Api.Services;

internal sealed class AiImageJobRetryHandler(
    IAiImageGenerationService images,
    IAiEntitlementService entitlementService,
    IAiOperationAvailabilityService availabilityService,
    IAiModelCatalogService modelCatalogService,
    AiRetryAttemptContext retryContext)
    : MeteredAiJobRetryHandler(
        AiOperations.ImageGeneration,
        entitlementService,
        availabilityService,
        modelCatalogService,
        retryContext)
{
    private static readonly HashSet<string> s_allowedProperties =
        ["prompt", "size", "aspectRatio", "background", "seed"];

    public override bool CanRetry(AiJob job, AiJobStatusSemantics status)
        => base.CanRetry(job, status)
            && TryParseReplayInput(job, out _);

    protected override AiOperationAvailabilityRequest CreateAvailabilityRequest(AiJob job)
        => new AiOperationAvailabilityRequest.Fixed(AiOperations.ImageGeneration, job.Model);

    protected override void ValidateRetryInputs(AiJob job)
    {
        _ = ParseReplayInput(job);
    }

    protected override bool IsCurrentRequestSupported(AiModelCatalog catalog, AiJob job)
    {
        if (!TryParseReplayInput(job, out ImageReplayInput input))
            return false;

        AiModelOption? model = ResolveModel(catalog, job);
        AiImageModelCapabilities? capabilities = model?.Image;
        if (capabilities is null)
            return true;
        return (!capabilities.AspectRatios.IsSpecified
                || capabilities.AspectRatios.Values.Contains(input.AspectRatio))
            && (input.Background is not { Length: > 0 } background
                || string.Equals(background, "auto", StringComparison.Ordinal)
                || !capabilities.Backgrounds.IsSpecified
                || capabilities.Backgrounds.Values.Contains(background))
            && (input.Seed is null || capabilities.SupportsSeed);
    }

    protected override async Task DispatchAsync(
        AiJob job,
        string idempotencyKey,
        bool isRepeat)
    {
        ImageReplayInput input = ParseReplayInput(job);
        await images.GenerateAsync(
            new AiImageGenerationRequest(
                input.Prompt,
                new AiImageAspectRatioId(input.AspectRatio),
                background: ResolveBackground(input),
                seed: input.Seed,
                model: job.Model,
                idempotencyKey: idempotencyKey),
            CancellationToken.None);
    }

    private static AiImageBackgroundId ResolveBackground(ImageReplayInput input)
    {
        string? background = input.Background;
        return string.IsNullOrWhiteSpace(background)
            ? default
            : new AiImageBackgroundId(background);
    }

    // Jobs recorded before the endpoint spoke ratios carry the fixed size they
    // were asked for. Mapping it back is what keeps a repeat the same shape.
    private static ImageReplayInput ParseReplayInput(AiJob job)
    {
        if (!TryParseReplayInput(job, out ImageReplayInput input))
            throw new InvalidOperationException("The retained image request cannot be replayed exactly.");
        return input;
    }

    private static bool TryParseReplayInput(AiJob job, out ImageReplayInput result)
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

        bool hasAspect = input.TryGetProperty("aspectRatio", out JsonElement aspectElement);
        bool hasSize = input.TryGetProperty("size", out JsonElement sizeElement);
        if (hasAspect == hasSize)
            return false;

        string aspectRatio = hasAspect
            ? aspectElement.ValueKind == JsonValueKind.String
                ? aspectElement.GetString() ?? string.Empty
                : string.Empty
            : hasSize
                ? sizeElement.ValueKind == JsonValueKind.String
                    ? sizeElement.GetString() switch
                    {
                        "1024x1024" => "1:1",
                        "1024x1536" => "2:3",
                        "1536x1024" => "3:2",
                        _ => string.Empty,
                    }
                    : string.Empty
                : "1:1";
        if (!AiReplayInputValidation.IsStructurallyValidAspectRatio(aspectRatio))
            return false;

        string? background = null;
        if (input.TryGetProperty("background", out JsonElement backgroundElement))
        {
            if (backgroundElement.ValueKind != JsonValueKind.String)
                return false;
            background = backgroundElement.GetString();
            if (background is not ("auto" or "opaque" or "transparent"))
                return false;
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

        result = new ImageReplayInput(promptValue, aspectRatio, background, seed);
        return true;
    }

    private static AiModelOption? ResolveModel(AiModelCatalog catalog, AiJob job)
    {
        ImmutableArray<AiModelOption> models = catalog.ModelsFor(AiOperations.ImageGeneration);
        if (models.IsDefaultOrEmpty)
            return null;
        if (job.Model is { Value.Length: > 0 } model)
            return models.FirstOrDefault(option => option.Id == model);
        return catalog.DefaultFor(AiOperations.ImageGeneration);
    }

    private readonly record struct ImageReplayInput(
        string Prompt,
        string AspectRatio,
        string? Background,
        int? Seed);
}

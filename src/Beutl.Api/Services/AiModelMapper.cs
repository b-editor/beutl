using System.Collections.Immutable;
using Beutl.Api.Clients;

namespace Beutl.Api.Services;

internal static class AiModelMapper
{
    public static AiEntitlements ToModel(EntitlementsResponse response)
    {
        if (!response.TryNormalize(out EntitlementsResponse? normalized))
            throw new AiException("The AI entitlement response is invalid.");
        EntitlementsResponse value = normalized!;

        return new AiEntitlements(
            value.Plan,
            value.SubscriptionStatus,
            ParseTimestamp(value.CurrentPeriodStart),
            ParseTimestamp(value.CurrentPeriodEnd),
            value.CancelAtPeriodEnd,
            value.CanUseAi,
            ToModel(value.Balance),
            new AiOperationAvailability(
                value.Availability.Select(pair =>
                    new KeyValuePair<AiOperationId, bool>(
                        new AiOperationId(pair.Key),
                        pair.Value))))
        {
            ModelAvailability = ToModel(value.ModelAvailability),
        };
    }

    private static AiModelAvailability ToModel(
        ImmutableDictionary<string, ImmutableDictionary<string, bool>>? response)
    {
        if (response is null || response.Count == 0)
            return AiModelAvailability.Empty;

        return new AiModelAvailability(
            response
                .Where(pair => pair.Value is not null)
                .Select(pair => new KeyValuePair<AiOperationId, ImmutableDictionary<AiModelId, bool>>(
                    new AiOperationId(pair.Key),
                    pair.Value.ToImmutableDictionary(
                        model => new AiModelId(model.Key),
                        model => model.Value))));
    }

    // Models the server did not describe well enough to offer are dropped
    // rather than shown unnamed: an entry whose id cannot be sent back is not a
    // choice.
    public static AiModelCatalog ToModel(
        AiCapabilitiesResponse response,
        IAiOperationCapabilitySchemaProvider? capabilitySchemas = null)
        => ToModel(
            response,
            (capabilitySchemas ?? BuiltInAiOperationCapabilitySchemaProvider.Instance)
                .GetSnapshot());

    public static AiModelCatalog ToModel(
        AiCapabilitiesResponse response,
        AiOperationCapabilitySchemaSnapshot capabilitySchemas)
    {
        if (response.Operations is null || response.Operations.Count == 0)
            return AiModelCatalog.Empty;

        (AiOperationId Operation,
            ImmutableArray<AiModelOption> Models,
            bool ModelsWereSpecified,
            AiModelCapabilitySchema Schema,
            AiOperationCapabilityResponse Capability)[]
            operations =
            response.Operations
                .Select(pair =>
                {
                    var operation = new AiOperationId(pair.Key);
                    AiModelCapabilitySchema schema = capabilitySchemas.GetSchema(operation);
                    return (
                        operation,
                        ToModelOptions(pair.Value, schema),
                        pair.Value.Models is not null,
                        schema,
                        pair.Value);
                })
                .ToArray();

        return new AiModelCatalog(
            operations
                .Where(item => !item.Models.IsDefaultOrEmpty)
                .Select(item => KeyValuePair.Create(item.Operation, item.Models)),
            // An operation the server described but left with no usable model is
            // explicitly unavailable. A missing operation stays absent here.
            operations
                .Where(item => item.ModelsWereSpecified && item.Models.IsDefaultOrEmpty)
                .Select(item => item.Operation),
            operations.Select(item => KeyValuePair.Create(item.Operation, item.Schema)),
            operations
                .Where(item => item.Schema == AiModelCapabilitySchema.Image)
                .Select(item => KeyValuePair.Create(
                    item.Operation,
                    ToImageReferenceLimits(item.Capability))))
        {
            CaptionTranslationLimits = ToCaptionTranslationLimits(response),
        };
    }

    private static AiImageReferenceLimits ToImageReferenceLimits(
        AiOperationCapabilityResponse operation)
        => operation.MaxReferenceImagesTotalBytes is > 0 and var maxTotalBytes
                ? new AiImageReferenceLimits(maxTotalBytes)
                : AiImageReferenceLimits.Default;

    private static AiCaptionTranslationLimits ToCaptionTranslationLimits(
        AiCapabilitiesResponse response)
    {
        if (!response.Operations.TryGetValue(
                AiOperations.CaptionTranslation.Value,
                out AiOperationCapabilityResponse? operation))
        {
            return AiCaptionTranslationLimits.Default;
        }

        return new AiCaptionTranslationLimits(
            operation.MaxSegments is > 0 and var maxSegments
                ? maxSegments
                : AiRequestLimits.MaxTranslationSegments,
            operation.MaxCharacters is > 0 and var maxCharacters
                ? maxCharacters
                : AiRequestLimits.MaxTranslationCharacters,
            operation.MaxRequestBytes is > 0 and var maxRequestBytes
                ? maxRequestBytes
                : AiRequestLimits.MaxTranslationRequestBytes);
    }

    // Called only for image operations. A model with no fields of its own still
    // inherits the operation dimensions; explicit operation emptiness remains
    // Unsupported rather than being widened to client defaults.
    private static AiImageModelCapabilities ToImageCapabilities(
        AiModelDescriptionResponse model,
        AiOperationCapabilityResponse capability)
    {
        return new AiImageModelCapabilities(
            NarrowDimension(ToStringDimension(model.AspectRatios), capability.AspectRatios),
            NarrowDimension(ToStringDimension(model.Backgrounds), capability.Backgrounds),
            model.Seed ?? true,
            model.MaxReferenceImages ?? AiRequestLimits.MaxImageReferences,
            model.Resolution ?? true);
    }

    // Called only for video generation. Omitted model fields inherit operation
    // dimensions, while omitted operation fields remain Unspecified.
    private static AiVideoModelCapabilities ToVideoCapabilities(
        AiModelDescriptionResponse model,
        AiOperationCapabilityResponse capability)
    {
        return Narrow(
            new AiVideoModelCapabilities(
                model.DurationsSeconds is { } durations
                    ? AiCapabilityDimension<int>.Supported(durations)
                    : AiCapabilityDimension<int>.Unspecified,
                ToStringDimension(model.Resolutions),
                ToStringDimension(model.AspectRatios),
                model.Audio ?? true,
                model.Seed ?? true,
                model.FirstFrame ?? true,
                model.LastFrame ?? true,
                model.PromptToVideo ?? true,
                model.InputReferences ?? false,
                Math.Clamp(model.MaxInputReferences ?? 0, 0, 9),
                Math.Clamp(model.MaxInputReferenceBytes ?? 0, 0, AiRequestLimits.MaxFrameUploadBytes),
                Math.Clamp(model.MaxVideoReferences ?? 0, 0, 3),
                Math.Clamp(model.MaxVideoReferenceBytes ?? 0, 0, AiVideoInputLimits.MaxSourceBytes),
                Math.Clamp(model.MaxAudioReferences ?? 0, 0, 1),
                Math.Clamp(model.MaxAudioReferenceBytes ?? 0, 0, AiVideoInputLimits.MaxAudioBytes),
                Math.Clamp(model.MaxSourceVideoBytes ?? AiVideoInputLimits.MaxSourceBytes, 0, AiVideoInputLimits.MaxSourceBytes),
                model.MinSourceVideoSeconds,
                model.MaxSourceVideoSeconds,
                Math.Clamp(model.MaxPromptLength ?? AiRequestLimits.MaxPromptLength, 1, AiRequestLimits.MaxPromptLength)),
            capability);
    }

    // A model that omits a list leaves the dimension Unspecified; blank entries are dropped.
    private static AiCapabilityDimension<string> ToStringDimension(ImmutableArray<string>? values)
        => values is { } present
            ? AiCapabilityDimension<string>.Supported(
                present.Where(value => !string.IsNullOrWhiteSpace(value)))
            : AiCapabilityDimension<string>.Unspecified;

    private static ImmutableArray<AiModelOption> ToModelOptions(
        AiOperationCapabilityResponse capability,
        AiModelCapabilitySchema schema)
    {
        if (capability.Models is not { IsDefaultOrEmpty: false } models)
            return [];

        return models
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .Select(model => new AiModelOption(
                new AiModelId(model.Id),
                string.IsNullOrWhiteSpace(model.DisplayName)
                    ? model.Id.Trim()
                    : model.DisplayName.Trim(),
                ToCostTier(model.CostTier),
                model.IsDefault,
                schema == AiModelCapabilitySchema.Video
                    ? ToVideoCapabilities(model, capability)
                    : null,
                schema == AiModelCapabilitySchema.Image
                    ? ToImageCapabilities(model, capability)
                    : null))
            .ToImmutableArray();
    }

    // A model's own lists narrowed to what the operation accepts, so a shape
    // the server would refuse never reaches the dialog. Order follows the
    // operation's, which runs from the smallest resolution upwards.
    private static AiVideoModelCapabilities Narrow(
        AiVideoModelCapabilities model,
        AiOperationCapabilityResponse capability)
    {
        AiCapabilityDimension<int> durations;
        if (!model.DurationsSeconds.IsSpecified
            && (capability.MinDurationSeconds is not null
                || capability.MaxDurationSeconds is not null))
        {
            int minimum = Math.Max(
                capability.MinDurationSeconds ?? AiRequestLimits.MinVideoDurationSeconds,
                AiRequestLimits.MinVideoDurationSeconds);
            int maximum = Math.Min(
                capability.MaxDurationSeconds ?? AiRequestLimits.MaxVideoDurationSeconds,
                AiRequestLimits.MaxVideoDurationSeconds);
            durations = minimum <= maximum
                ? AiCapabilityDimension<int>.Supported(
                    Enumerable.Range(minimum, maximum - minimum + 1))
                : AiCapabilityDimension<int>.Unsupported;
        }
        else
        {
            durations = !model.DurationsSeconds.IsSpecified
                ? model.DurationsSeconds
                : AiCapabilityDimension<int>.Supported(model.DurationsSeconds.Values.Where(seconds =>
                    AiRequestLimits.IsValidVideoDurationSeconds(seconds)
                    && seconds >= (capability.MinDurationSeconds ?? int.MinValue)
                    && seconds <= (capability.MaxDurationSeconds ?? int.MaxValue)));
        }
        return model with
        {
            DurationsSeconds = durations,
            Resolutions = NarrowDimension(model.Resolutions, capability.Resolutions),
            AspectRatios = NarrowDimension(model.AspectRatios, capability.AspectRatios),
        };
    }

    private static AiCapabilityDimension<string> NarrowDimension(
        AiCapabilityDimension<string> model,
        ImmutableArray<string>? operation)
    {
        if (operation is null)
            return model;
        if (operation.Value.IsEmpty)
            return AiCapabilityDimension<string>.Unsupported;
        ImmutableArray<string> offered = operation.Value;
        if (!model.IsSpecified)
            return AiCapabilityDimension<string>.Supported(offered);
        if (model.Values.IsEmpty)
            return AiCapabilityDimension<string>.Unsupported;
        return AiCapabilityDimension<string>.Supported(offered.Where(model.Values.Contains));
    }

    // An unrecognized tier is reported as none rather than guessed at: the
    // label is the only thing said about relative cost, and a wrong one would
    // send a user to the pricier model believing it is the cheaper.
    private static AiModelCostTier? ToCostTier(string? value)
        => value switch
        {
            "low" => AiModelCostTier.Low,
            "medium" => AiModelCostTier.Medium,
            "high" => AiModelCostTier.High,
            _ => null,
        };

    public static AiBalance ToModel(AiBalanceResponse response)
    {
        if (!response.TryNormalize(out AiBalanceResponse? normalized))
            throw new AiException("The AI balance response is invalid.");
        AiBalanceResponse value = normalized!;
        return new AiBalance(
            ToModel(value.MonthlyUsage),
            value.AdditionalCredits,
            value.HasAdditionalCreditDebt);
    }

    internal static AiMonthlyUsage ToModel(AiMonthlyUsageResponse response)
        => new(
            response.UsedPercent,
            response.RemainingPercent,
            response.IsExhausted);

    public static AiImageResult ToModel(AiImageResponse response)
        => new(
            ToOptionalJobId(response.JobId),
            new AiContentId(response.FileId),
            ParseContentUri(response.Url),
            ToContentMetadata(response.FileName, response.ContentType));

    public static AiVideoGenerationResult ToModel(CreateAiVideoResponse response)
        => new(
            new AiJobId(response.JobId),
            new AiJobStatusId(response.Status));

    public static AiVideoJob ToModel(AiVideoJobResponse response)
        => new(
            new AiJobId(response.JobId),
            new AiJobStatusId(response.Status),
            string.IsNullOrWhiteSpace(response.FileId) ? null : new AiContentId(response.FileId),
            string.IsNullOrWhiteSpace(response.Url) ? null : ParseContentUri(response.Url),
            response.Error,
            ToContentMetadata(response.FileName, response.ContentType));

    public static AiTranscriptionResponse ToModel(AiTranscriptionResponseDto response)
        => new(
            ToOptionalJobId(response.JobId),
            response.Segments.Select(segment => new AiTranscriptionSegment
            {
                Start = segment.Start,
                End = segment.End,
                Text = segment.Text,
            }).ToArray(),
            response.Language,
            response.Words?.Select(word => new AiTranscriptionWord
            {
                Start = word.Start,
                End = word.End,
                Word = word.Word,
            }).ToArray());

    public static AiCaptionTranslationResponse ToModel(AiCaptionTranslationResponseDto response)
        => new(
            ToOptionalJobId(response.JobId),
            response.Segments.Select(segment => new AiCaptionTranslationSegment
            {
                Id = segment.Id,
                Text = segment.Text,
                Context = segment.Context is null
                    ? null
                    : new AiCaptionTranslationSegmentContext(
                        segment.Context.GroupId,
                        segment.Context.PartIndex,
                        TimeSpan.FromSeconds(segment.Context.Start),
                        TimeSpan.FromSeconds(segment.Context.End)),
            }).ToArray());

    public static AiJob ToModel(AiJobHistoryResponse response)
        => new(
            new AiJobId(response.Id),
            new AiJobKindId(response.Kind),
            new AiJobStatusId(response.Status),
            response.InputParams?.Clone(),
            string.IsNullOrWhiteSpace(response.FileId) ? null : new AiContentId(response.FileId),
            string.IsNullOrWhiteSpace(response.Url) ? null : ParseContentUri(response.Url),
            response.Error,
            response.CanRetry,
            response.CreatedAt.ToUniversalTime(),
            response.UpdatedAt.ToUniversalTime(),
            ToContentMetadata(response.FileName, response.ContentType))
        {
            Model = string.IsNullOrWhiteSpace(response.Model)
                ? null
                : new AiModelId(response.Model),
        };

    private static AiJobId? ToOptionalJobId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new AiJobId(value);

    private static Uri ParseContentUri(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            ? uri
            : throw new AiException("The AI response contains an invalid content URI.");

    private static AiContentMetadata? ToContentMetadata(string? fileName, string? contentType)
        => string.IsNullOrWhiteSpace(fileName) && string.IsNullOrWhiteSpace(contentType)
            ? null
            : new AiContentMetadata(fileName, contentType);

    private static DateTimeOffset? ParseTimestamp(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.TryParse(value, out DateTimeOffset timestamp)
                ? timestamp.ToUniversalTime()
                : throw new AiException("The AI entitlement response contains an invalid timestamp.");
}

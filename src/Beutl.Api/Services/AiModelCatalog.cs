using System.Collections.Immutable;

namespace Beutl.Api.Services;

/// <summary>
/// How one model compares in cost with the others offered for the same
/// operation. The server publishes an ordering rather than a price, so this is
/// all a client can say about what a choice will spend.
/// </summary>
public enum AiModelCostTier
{
    Low,
    Medium,
    High,
}

/// <summary>
/// One dimension of values a model accepts. Unspecified means the provider
/// omitted the dimension, Unsupported means it explicitly accepts no value,
/// and Supported carries the accepted values.
/// </summary>
/// <remarks>
/// <see langword="default"/> is identical to <see cref="Unspecified"/>.
/// Both unspecified and unsupported dimensions expose an empty <see cref="Values"/>
/// array; use <see cref="IsSpecified"/> to distinguish them. Calling
/// <see cref="Supported(IEnumerable{T})"/> with an empty sequence produces the
/// same value as <see cref="Unsupported"/>. Equality compares the specified
/// state and, for a specified dimension, the values in order.
/// </remarks>
public readonly struct AiCapabilityDimension<T> : IEquatable<AiCapabilityDimension<T>>
    where T : notnull
{
    private readonly ImmutableArray<T> _values;

    private AiCapabilityDimension(ImmutableArray<T> values, bool isSpecified)
    {
        _values = values;
        IsSpecified = isSpecified;
    }

    /// <summary>Gets the accepted values. Unspecified and unsupported dimensions return an empty array.</summary>
    public ImmutableArray<T> Values => _values.IsDefault ? [] : _values;

    /// <summary>Gets whether the server explicitly described this dimension.</summary>
    /// <remarks><see langword="false"/> means <see cref="Unspecified"/>; <see langword="true"/> with no values means <see cref="Unsupported"/>.</remarks>
    public bool IsSpecified { get; }

    /// <summary>Gets the default dimension, meaning that the server did not specify support.</summary>
    public static AiCapabilityDimension<T> Unspecified => default;

    /// <summary>Creates a dimension containing the values accepted by a model.</summary>
    /// <param name="values">The accepted values, in provider order. An empty sequence means explicitly unsupported.</param>
    /// <returns>A specified dimension whose <see cref="Values"/> are a defensive immutable copy.</returns>
    public static AiCapabilityDimension<T> Supported(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new(values.ToImmutableArray(), true);
    }

    /// <summary>Gets the explicitly unsupported dimension.</summary>
    public static AiCapabilityDimension<T> Unsupported { get; } =
        new([], true);

    /// <summary>Compares two dimensions by specified state and, when specified, ordered values.</summary>
    public bool Equals(AiCapabilityDimension<T> other)
    {
        if (IsSpecified != other.IsSpecified)
            return false;
        return !IsSpecified || Values.SequenceEqual(other.Values);
    }

    /// <summary>Determines whether this dimension equals another object.</summary>
    public override bool Equals(object? obj)
        => obj is AiCapabilityDimension<T> other && Equals(other);

    /// <summary>Returns a hash code based on specified state and ordered values.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(IsSpecified);
        if (IsSpecified)
        {
            foreach (T value in Values)
                hash.Add(value);
        }
        return hash.ToHashCode();
    }

    /// <summary>Determines whether two dimensions are equal.</summary>
    public static bool operator ==(
        AiCapabilityDimension<T> left,
        AiCapabilityDimension<T> right)
        => left.Equals(right);

    /// <summary>Determines whether two dimensions differ.</summary>
    public static bool operator !=(
        AiCapabilityDimension<T> left,
        AiCapabilityDimension<T> right)
        => !left.Equals(right);
}

/// <summary>
/// The aggregate byte budget for the reference pictures in one image request.
/// A default value is the client fallback used when an older server publishes
/// no limit.
/// </summary>
public readonly struct AiImageReferenceLimits : IEquatable<AiImageReferenceLimits>
{
    private readonly long _maxTotalBytes;

    public AiImageReferenceLimits(long maxTotalBytes)
    {
        if (maxTotalBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));
        _maxTotalBytes = maxTotalBytes;
    }

    public long MaxTotalBytes => _maxTotalBytes == 0
        ? AiRequestLimits.MaxImageReferencesTotalBytes
        : _maxTotalBytes;

    public static AiImageReferenceLimits Default => default;

    public bool Equals(AiImageReferenceLimits other)
        => MaxTotalBytes == other.MaxTotalBytes;

    public override bool Equals(object? obj)
        => obj is AiImageReferenceLimits other && Equals(other);

    public override int GetHashCode() => MaxTotalBytes.GetHashCode();

    public static bool operator ==(AiImageReferenceLimits left, AiImageReferenceLimits right)
        => left.Equals(right);

    public static bool operator !=(AiImageReferenceLimits left, AiImageReferenceLimits right)
        => !left.Equals(right);
}

/// <summary>
/// The server-published shape and serialized-body budget for one caption
/// translation request. A default value carries the client fallback for an
/// older server that publishes none of these fields.
/// </summary>
public readonly struct AiCaptionTranslationLimits : IEquatable<AiCaptionTranslationLimits>
{
    private readonly int _maxSegments;
    private readonly int _maxCharacters;
    private readonly int _maxRequestBytes;

    public AiCaptionTranslationLimits(
        int maxSegments,
        int maxCharacters,
        int maxRequestBytes)
    {
        if (maxSegments <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSegments));
        if (maxCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        if (maxRequestBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRequestBytes));
        _maxSegments = maxSegments;
        _maxCharacters = maxCharacters;
        _maxRequestBytes = maxRequestBytes;
    }

    public int MaxSegments => _maxSegments == 0
        ? AiRequestLimits.MaxTranslationSegments
        : _maxSegments;

    public int MaxCharacters => _maxCharacters == 0
        ? AiRequestLimits.MaxTranslationCharacters
        : _maxCharacters;

    public int MaxRequestBytes => _maxRequestBytes == 0
        ? AiRequestLimits.MaxTranslationRequestBytes
        : _maxRequestBytes;

    public static AiCaptionTranslationLimits Default => default;

    public bool Equals(AiCaptionTranslationLimits other)
        => MaxSegments == other.MaxSegments
            && MaxCharacters == other.MaxCharacters
            && MaxRequestBytes == other.MaxRequestBytes;

    public override bool Equals(object? obj)
        => obj is AiCaptionTranslationLimits other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(MaxSegments, MaxCharacters, MaxRequestBytes);

    public static bool operator ==(
        AiCaptionTranslationLimits left,
        AiCaptionTranslationLimits right)
        => left.Equals(right);

    public static bool operator !=(
        AiCaptionTranslationLimits left,
        AiCaptionTranslationLimits right)
        => !left.Equals(right);
}

public sealed record AiVideoModelCapabilities(
    AiCapabilityDimension<int> DurationsSeconds,
    AiCapabilityDimension<string> Resolutions,
    AiCapabilityDimension<string> AspectRatios,
    bool SupportsAudio,
    bool SupportsSeed,
    bool SupportsFirstFrame = true,
    bool SupportsLastFrame = true,
    bool SupportsPromptToVideo = true,
    bool SupportsInputReferences = false,
    int MaxInputReferences = 0,
    long MaxInputReferenceBytes = 0,
    int MaxVideoReferences = 0,
    long MaxVideoReferenceBytes = 0,
    int MaxAudioReferences = 0,
    long MaxAudioReferenceBytes = 0,
    long MaxSourceVideoBytes = AiVideoInputLimits.MaxSourceBytes,
    double? MinSourceVideoSeconds = null,
    double? MaxSourceVideoSeconds = null,
    int MaxPromptLength = AiRequestLimits.MaxPromptLength)
{
    public static AiVideoModelCapabilities Unrestricted { get; } =
        new(
            AiCapabilityDimension<int>.Unspecified,
            AiCapabilityDimension<string>.Unspecified,
            AiCapabilityDimension<string>.Unspecified,
            true,
            true);

    /// <summary>
    /// False for a model that shares no resolution or shape with what the
    /// server accepts. The lists are already narrowed to that when they are
    /// read, so nothing left on one of them means every request naming this
    /// model would be refused, and offering it is worse than hiding it.
    /// </summary>
    public bool CanServeAnything()
        => (!DurationsSeconds.IsSpecified || !DurationsSeconds.Values.IsEmpty)
            && (!Resolutions.IsSpecified || !Resolutions.Values.IsEmpty)
            && (!AspectRatios.IsSpecified || !AspectRatios.Values.IsEmpty);
}

/// <summary>
/// What one image model will take. GPT Image-1 renders 1:1, 3:2 and 2:3 and
/// refuses everything else; the backgrounds differ per model as well, and only
/// some take a seed or accept a picture to work from — which every edit
/// depends on. Aspect ratios and backgrounds use <see cref="AiCapabilityDimension{T}"/>
/// so omitted, explicitly empty and narrowed lists remain distinct.
/// </summary>
public sealed record AiImageModelCapabilities(
    AiCapabilityDimension<string> AspectRatios,
    AiCapabilityDimension<string> Backgrounds,
    bool SupportsSeed,
    int MaxReferenceImages,
    bool SupportsResolution = true)
{
    public static AiImageModelCapabilities Unrestricted { get; } =
        new(
            AiCapabilityDimension<string>.Unspecified,
            AiCapabilityDimension<string>.Unspecified,
            true,
            AiRequestLimits.MaxImageReferences);

    /// <summary>
    /// False for a model that shares no shape with what the server accepts, or
    /// that cannot be handed the picture an edit is made of.
    /// </summary>
    /// <param name="requiresResolution">
    /// The operation asks for a size, which is what an upscale is; a model that
    /// publishes no sizes cannot serve it.
    /// </param>
    /// <param name="requiredBackground">
    /// The background the operation always asks for. Removing a background is
    /// asking for a transparent one, and a model offering only auto and opaque
    /// would refuse every such request.
    /// </param>
    public bool CanServeAnything(
        bool requiresReferenceImages,
        bool requiresResolution = false,
        string? requiredBackground = null)
        => (!AspectRatios.IsSpecified || !AspectRatios.Values.IsEmpty)
           && (!Backgrounds.IsSpecified || !Backgrounds.Values.IsEmpty)
           && (!requiresReferenceImages || MaxReferenceImages > 0)
           && (!requiresResolution || SupportsResolution)
           && (requiredBackground is not { Length: > 0 } background
               || !Backgrounds.IsSpecified
               || Backgrounds.Values.Contains(background));
}

public sealed record AiModelOption(
    AiModelId Id,
    string DisplayName,
    AiModelCostTier? CostTier,
    bool IsDefault,
    AiVideoModelCapabilities? Video = null,
    AiImageModelCapabilities? Image = null);

/// <summary>
/// The models each operation offers. Empty for an operation the server did not
/// report, which a request answers by naming no model at all and letting the
/// server pick its default.
/// </summary>
public sealed class AiModelCatalog
{
    public static AiModelCatalog Empty { get; } =
        new(ImmutableDictionary<AiOperationId, ImmutableArray<AiModelOption>>.Empty);

    public AiModelCatalog(
        IEnumerable<KeyValuePair<AiOperationId, ImmutableArray<AiModelOption>>> operations,
        IEnumerable<AiOperationId>? withoutModels = null,
        IEnumerable<KeyValuePair<AiOperationId, AiModelCapabilitySchema>>? capabilitySchemas = null,
        IEnumerable<KeyValuePair<AiOperationId, AiImageReferenceLimits>>? imageReferenceLimits = null)
    {
        ArgumentNullException.ThrowIfNull(operations);
        Operations = operations.ToImmutableDictionary(
            pair => pair.Key,
            pair => NormalizeModels(pair.Value));
        WithoutModels = withoutModels?.ToImmutableHashSet() ?? [];
        if (WithoutModels.Overlaps(Operations.Keys))
        {
            throw new ArgumentException(
                "An operation cannot both offer models and explicitly offer no models.",
                nameof(withoutModels));
        }
        ImmutableDictionary<AiOperationId, AiModelCapabilitySchema> schemas =
            capabilitySchemas?.ToImmutableDictionary(pair => pair.Key, pair => pair.Value)
            ?? ImmutableDictionary<AiOperationId, AiModelCapabilitySchema>.Empty;
        if (schemas.Any(pair => !Enum.IsDefined(pair.Value)))
            throw new ArgumentException("A model capability schema is unsupported.", nameof(capabilitySchemas));
        CapabilitySchemas = schemas;
        ImageReferenceLimitsByOperation = imageReferenceLimits?.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value)
            ?? ImmutableDictionary<AiOperationId, AiImageReferenceLimits>.Empty;

        static ImmutableArray<AiModelOption> NormalizeModels(
            ImmutableArray<AiModelOption> models)
        {
            if (models.IsDefault)
                return [];
            var modelIds = new HashSet<AiModelId>();
            foreach (AiModelOption? model in models)
            {
                if (model is null || model.Id.Value.Length == 0)
                {
                    throw new ArgumentException(
                        "Every catalog model must have a non-empty identifier.",
                        nameof(operations));
                }
                if (!modelIds.Add(model.Id))
                {
                    throw new ArgumentException(
                        "Model identifiers must be unique within an operation.",
                        nameof(operations));
                }
                if (model.Video?.DurationsSeconds is { IsSpecified: true } durations
                    && durations.Values.Any(seconds =>
                        !AiRequestLimits.IsValidVideoDurationSeconds(seconds)))
                {
                    throw new ArgumentException(
                        $"Video duration capabilities must be between "
                        + $"{AiRequestLimits.MinVideoDurationSeconds} and "
                        + $"{AiRequestLimits.MaxVideoDurationSeconds} seconds.",
                        nameof(operations));
                }
                if (model.Video is { } video)
                {
                    ValidateCapabilityIdentifiers(video.Resolutions, "video resolution");
                    ValidateCapabilityIdentifiers(video.AspectRatios, "video aspect ratio");
                }
                if (model.Image is { } image)
                {
                    ValidateCapabilityIdentifiers(image.AspectRatios, "image aspect ratio");
                    ValidateCapabilityIdentifiers(image.Backgrounds, "image background");
                }
            }

            return models;

            static void ValidateCapabilityIdentifiers(
                AiCapabilityDimension<string> dimension,
                string dimensionName)
            {
                if (!dimension.IsSpecified)
                    return;

                var normalizedValues = new HashSet<string>(StringComparer.Ordinal);
                foreach (string? value in dimension.Values)
                {
                    string normalized;
                    try
                    {
                        normalized = AiIdentifier.Normalize(value!, dimensionName);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new ArgumentException(
                            $"Every {dimensionName} capability must be a valid identifier.",
                            nameof(operations),
                            ex);
                    }

                    if (!string.Equals(value, normalized, StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            $"Every {dimensionName} capability must use its canonical identifier.",
                            nameof(operations));
                    }

                    if (!normalizedValues.Add(normalized))
                    {
                        throw new ArgumentException(
                            $"{dimensionName} capabilities must be unique after normalization.",
                            nameof(operations));
                    }
                }
            }
        }
    }

    public ImmutableDictionary<AiOperationId, ImmutableArray<AiModelOption>> Operations { get; }

    /// <summary>
    /// Operations the server named and offered no model for.
    /// </summary>
    /// <remarks>
    /// Told apart from an operation the server said nothing about, which is how
    /// a server that predates per-operation models reads and which a request
    /// answers by naming no model at all. Named with nothing behind it means the
    /// operation has been stopped, and a request would be refused however it is
    /// shaped.
    /// </remarks>
    public ImmutableHashSet<AiOperationId> WithoutModels { get; }

    public ImmutableDictionary<AiOperationId, AiModelCapabilitySchema> CapabilitySchemas { get; }

    public bool OffersNoModel(AiOperationId operation) => WithoutModels.Contains(operation);

    public AiModelCapabilitySchema GetCapabilitySchema(AiOperationId operation)
        => CapabilitySchemas.GetValueOrDefault(operation, AiModelCapabilitySchema.Generic);

    /// <summary>
    /// Resolved aggregate reference-picture budgets keyed by image operation.
    /// </summary>
    public ImmutableDictionary<AiOperationId, AiImageReferenceLimits>
        ImageReferenceLimitsByOperation
    { get; }

    /// <summary>
    /// Gets the reference-picture budget for one operation, using the client fallback when an
    /// older server did not publish it.
    /// </summary>
    public AiImageReferenceLimits GetImageReferenceLimits(AiOperationId operation)
        => ImageReferenceLimitsByOperation.GetValueOrDefault(
            operation,
            AiImageReferenceLimits.Default);

    /// <summary>
    /// The caption-translation request limits published by the server. A
    /// default value carries the client fallback for an older server.
    /// </summary>
    public AiCaptionTranslationLimits CaptionTranslationLimits { get; init; }

    public ImmutableArray<AiModelOption> ModelsFor(AiOperationId operation)
        => Operations.TryGetValue(operation, out ImmutableArray<AiModelOption> models)
            ? models
            : [];

    public AiModelOption? DefaultFor(AiOperationId operation)
    {
        ImmutableArray<AiModelOption> models = ModelsFor(operation);
        if (models.IsDefaultOrEmpty)
            return null;
        foreach (AiModelOption model in models)
        {
            if (model.IsDefault)
                return model;
        }

        return models[0];
    }
}

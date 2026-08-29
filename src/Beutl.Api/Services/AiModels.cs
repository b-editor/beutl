using System.Collections.Immutable;
using Beutl.Api.Clients;

namespace Beutl.Api.Services;

public readonly struct AiOperationId : IEquatable<AiOperationId>
{
    private readonly string? _value;

    public AiOperationId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiOperationId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiOperationId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiOperationId left, AiOperationId right) => left.Equals(right);

    public static bool operator !=(AiOperationId left, AiOperationId right) => !left.Equals(right);
}

/// <summary>
/// One of the models an operation may run on, as registered on the server. The
/// list is not known at build time, so it is never a fixed set here.
/// </summary>
public readonly struct AiModelId : IEquatable<AiModelId>
{
    private readonly string? _value;

    public AiModelId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiModelId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiModelId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiModelId left, AiModelId right) => left.Equals(right);

    public static bool operator !=(AiModelId left, AiModelId right) => !left.Equals(right);
}

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
    bool SupportsLastFrame = true)
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
        IEnumerable<AiOperationId>? withoutModels = null)
    {
        ArgumentNullException.ThrowIfNull(operations);
        Operations = operations.ToImmutableDictionary(pair => pair.Key, pair => pair.Value);
        WithoutModels = withoutModels?.ToImmutableHashSet() ?? [];
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

    public bool OffersNoModel(AiOperationId operation) => WithoutModels.Contains(operation);

    /// <summary>
    /// The aggregate reference-picture budget published for image generation.
    /// A default value carries the client fallback for an older server.
    /// </summary>
    public AiImageReferenceLimits ImageReferenceLimits { get; init; }

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

public static class AiOperations
{
    public static AiOperationId ImageGeneration { get; } = new("image.generate");

    public static AiOperationId VideoGeneration { get; } = new("video.generate");

    public static AiOperationId Transcription { get; } = new("audio.transcribe");

    public static AiOperationId CaptionTranslation { get; } = new("subtitle.translate");

    public static AiOperationId ImageEdit(AiImageEditTaskId task)
        => new($"image.edit.{task.Value}");
}

public abstract record AiOperationAvailabilityRequest
{
    private AiOperationAvailabilityRequest(AiOperationId operation, AiModelId? model)
    {
        if (operation.Value.Length == 0)
            throw new ArgumentException("An AI operation is required.", nameof(operation));
        Operation = operation;
        Model = model is { Value.Length: > 0 } ? model : null;
    }

    public AiOperationId Operation { get; }

    /// <summary>
    /// The model the question is about. Null asks about the operation's
    /// default, since that is what a request naming no model would run on.
    /// </summary>
    public AiModelId? Model { get; }

    public sealed record Fixed : AiOperationAvailabilityRequest
    {
        public Fixed(AiOperationId operation, AiModelId? model = null)
            : base(operation, model)
        {
            if (operation != AiOperations.ImageGeneration
                && !operation.Value.StartsWith("image.edit.", StringComparison.Ordinal))
            {
                throw new ArgumentException("The operation does not use fixed availability.", nameof(operation));
            }
        }
    }

    public sealed record Video : AiOperationAvailabilityRequest
    {
        public Video(int durationSeconds, AiModelId? model = null)
            : base(AiOperations.VideoGeneration, model)
        {
            DurationSeconds = AiRequestLimits.ValidateVideoDurationSeconds(
                durationSeconds,
                nameof(durationSeconds));
        }

        public int DurationSeconds { get; }
    }

    public sealed record Transcription : AiOperationAvailabilityRequest
    {
        public Transcription(double durationSeconds, AiModelId? model = null)
            : base(AiOperations.Transcription, model)
        {
            if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            DurationSeconds = durationSeconds;
        }

        public double DurationSeconds { get; }
    }

    public sealed record Translation : AiOperationAvailabilityRequest
    {
        public Translation(
            int characterCount,
            AiModelId? model = null,
            AiCaptionTranslationLimits? limits = null)
            : base(AiOperations.CaptionTranslation, model)
        {
            AiCaptionTranslationLimits effectiveLimits =
                limits ?? AiCaptionTranslationLimits.Default;
            if (characterCount <= 0 || characterCount > effectiveLimits.MaxCharacters)
                throw new ArgumentOutOfRangeException(nameof(characterCount));
            CharacterCount = characterCount;
        }

        public int CharacterCount { get; }
    }
}

public static class AiRequestLimits
{
    private static readonly ImmutableHashSet<string> s_iso6391LanguageCodes =
        ("aa ab ae af ak am an ar as av ay az ba be bg bh bi bm bn bo br bs "
        + "ca ce ch co cr cs cu cv cy da de dv dz ee el en eo es et eu fa ff "
        + "fi fj fo fr fy ga gd gl gn gu gv ha he hi ho hr ht hu hy hz ia id "
        + "ie ig ii ik io is it iu ja jv ka kg ki kj kk kl km kn ko kr ks ku "
        + "kv kw ky la lb lg li ln lo lt lu lv mg mh mi mk ml mn mr ms mt my "
        + "na nb nd ne ng nl nn no nr nv ny oc oj om or os pa pi pl ps pt qu "
        + "rm rn ro ru rw sa sc sd se sg si sk sl sm sn so sq sr ss st su sv "
        + "sw ta te tg th ti tk tl tn to tr ts tt tw ty ug uk ur uz ve vi vo "
        + "wa wo xh yi yo za zh zu")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .ToImmutableHashSet(StringComparer.Ordinal);

    public const int MaxPromptLength = 4_000;

    public const int MaxTranslationSegments = 200;

    public const int MaxTranslationCharacters = 20_000;

    public const int MaxTranslationRequestBytes = 128 * 1024;

    public const long MaxFrameUploadBytes = 5L * 1024 * 1024;

    public const long MaxImageUploadBytes = 20L * 1024 * 1024;

    // What the transcription endpoint takes in one upload. Speech is sent as
    // 16 kHz mono 16-bit PCM, so this is a little under fourteen minutes of it:
    // audio longer than that has to be split before it is sent, and a caller
    // that sends it anyway is refused after the whole upload has gone out.
    public const long MaxTranscriptionUploadBytes = 25L * 1024 * 1024;

    // What the server is priced for. Each model publishes its own count and the
    // smaller of the two is what may be sent; this is also what a server that
    // publishes nothing is read as.
    public const int MaxImageReferences = 4;

    // What all the reference pictures of one request may come to together, for
    // a server that publishes no figure of its own. The per-picture limit taken
    // four times over is more than the server can hold: every picture is kept
    // raw, again as base64 and again through JSON, so the fallback is what one
    // picture was already allowed to be.
    public const long MaxImageReferencesTotalBytes = MaxImageUploadBytes;

    // The provider accepts a signed 32-bit seed. Bounding it here keeps the
    // same number intact through every JSON encoder on the way.
    public const int MinSeed = 0;

    public const int MaxSeed = int.MaxValue;

    // The span the server considers at all. Which whole seconds within it a
    // given model takes is published per model: Veo 3.1 takes 4, 6 or 8 and
    // Seedance 2.5 anything from 4 to 30, so a fixed three would offer lengths
    // one model refuses and hide most of another's.
    public const int MinVideoDurationSeconds = 1;

    public const int MaxVideoDurationSeconds = 60;

    internal static int ValidateVideoDurationSeconds(
        int durationSeconds,
        string parameterName)
    {
        if (durationSeconds is < MinVideoDurationSeconds or > MaxVideoDurationSeconds)
            throw new ArgumentOutOfRangeException(parameterName);
        return durationSeconds;
    }

    internal static string ValidatePrompt(string prompt, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt, parameterName);
        string normalized = prompt.Trim();
        if (normalized.Length > MaxPromptLength)
        {
            throw new ArgumentException(
                $"The final prompt cannot exceed {MaxPromptLength} characters.",
                parameterName);
        }

        return normalized;
    }

    internal static bool IsSafeTranslationIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64)
            return false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool alphaNumeric = c is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9';
            if (i == 0 ? !alphaNumeric : !alphaNumeric && c is not ('_' or '-'))
                return false;
        }

        return true;
    }

    internal static bool IsIso6391LanguageCode(string value)
        => s_iso6391LanguageCodes.Contains(value);

    internal static string? ValidateOptionalPrompt(string? prompt, string parameterName)
        => string.IsNullOrWhiteSpace(prompt) ? null : ValidatePrompt(prompt, parameterName);

    internal static int? ValidateOptionalSeed(int? seed, string parameterName)
    {
        if (seed is null) return null;
        if (seed.Value is < MinSeed or > MaxSeed)
            throw new ArgumentOutOfRangeException(parameterName);
        return seed;
    }

    // The server holds an idempotency key to printable ASCII and refuses the
    // request outright when it does not match, so a key that could never be
    // accepted is caught here rather than after the whole upload has gone out.
    internal static string? ValidateOptionalIdempotencyKey(string? key, string parameterName)
    {
        if (key is null)
            return null;
        if (key.Length is 0 or > 255)
            throw new ArgumentException("The idempotency key length is invalid.", parameterName);
        foreach (char character in key)
        {
            if (character is < '\u0021' or > '\u007e')
            {
                throw new ArgumentException(
                    "An idempotency key may only contain printable ASCII.",
                    parameterName);
            }
        }

        return key;
    }

    // A default-constructed AiModelId carries no id, and sending an empty
    // string would be refused as an unknown model. It means "no choice made",
    // so it is normalized to null and the server picks its own default.
    internal static AiModelId? ValidateOptionalModel(AiModelId? model, string parameterName)
    {
        _ = parameterName;
        return model is { Value.Length: > 0 } ? model : null;
    }
}

// The server is asked for a shape, not a pixel count: "16:9" and "9:16" are the
// ones a video editor needs and no fixed size could express them.
public readonly struct AiImageAspectRatioId : IEquatable<AiImageAspectRatioId>
{
    private readonly string? _value;

    public AiImageAspectRatioId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiImageAspectRatioId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiImageAspectRatioId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiImageAspectRatioId left, AiImageAspectRatioId right) => left.Equals(right);

    public static bool operator !=(AiImageAspectRatioId left, AiImageAspectRatioId right) => !left.Equals(right);
}

// Named rather than a flag: the server publishes which backgrounds each model
// takes, and a model that fills a background in is not the same as one that
// cuts it out.
public readonly struct AiImageBackgroundId : IEquatable<AiImageBackgroundId>
{
    private readonly string? _value;

    public AiImageBackgroundId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiImageBackgroundId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiImageBackgroundId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiImageBackgroundId left, AiImageBackgroundId right) => left.Equals(right);

    public static bool operator !=(AiImageBackgroundId left, AiImageBackgroundId right) => !left.Equals(right);
}

public readonly struct AiVideoAspectRatioId : IEquatable<AiVideoAspectRatioId>
{
    private readonly string? _value;

    public AiVideoAspectRatioId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiVideoAspectRatioId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiVideoAspectRatioId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiVideoAspectRatioId left, AiVideoAspectRatioId right) => left.Equals(right);

    public static bool operator !=(AiVideoAspectRatioId left, AiVideoAspectRatioId right) => !left.Equals(right);
}

public readonly struct AiImageEditTaskId : IEquatable<AiImageEditTaskId>
{
    private readonly string? _value;

    public AiImageEditTaskId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiImageEditTaskId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiImageEditTaskId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiImageEditTaskId left, AiImageEditTaskId right) => left.Equals(right);

    public static bool operator !=(AiImageEditTaskId left, AiImageEditTaskId right) => !left.Equals(right);
}

public readonly struct AiVideoResolutionId : IEquatable<AiVideoResolutionId>
{
    private readonly string? _value;

    public AiVideoResolutionId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiVideoResolutionId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiVideoResolutionId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiVideoResolutionId left, AiVideoResolutionId right) => left.Equals(right);

    public static bool operator !=(AiVideoResolutionId left, AiVideoResolutionId right) => !left.Equals(right);
}

public sealed record AiImageGenerationRequest
{
    public AiImageGenerationRequest(
        string prompt,
        AiImageAspectRatioId aspectRatio,
        AiImageBackgroundId background = default,
        int? seed = null,
        IReadOnlyList<AiUploadSource>? references = null,
        AiModelId? model = null,
        string? idempotencyKey = null,
        AiImageReferenceLimits? referenceLimits = null)
    {
        if (aspectRatio.Value.Length == 0)
            throw new ArgumentException("An image aspect ratio is required.", nameof(aspectRatio));
        if (references is { Count: > AiRequestLimits.MaxImageReferences })
        {
            throw new ArgumentException(
                $"At most {AiRequestLimits.MaxImageReferences} reference pictures may guide one generation.",
                nameof(references));
        }

        if (references?.Any(reference => reference is null) == true)
            throw new ArgumentException("Reference pictures cannot contain null.", nameof(references));
        if (references?.Any(reference => reference.Length > AiRequestLimits.MaxImageUploadBytes) == true)
            throw new AiFileTooLargeException();
        AiImageReferenceLimits effectiveLimits =
            referenceLimits ?? AiImageReferenceLimits.Default;
        if (references is not null)
        {
            long total = 0;
            foreach (AiUploadSource reference in references)
            {
                if (reference.Length > effectiveLimits.MaxTotalBytes - total)
                    throw new AiFileTooLargeException();
                total += reference.Length;
            }
        }

        Prompt = AiRequestLimits.ValidatePrompt(prompt, nameof(prompt));
        AspectRatio = aspectRatio;
        Background = background;
        Seed = AiRequestLimits.ValidateOptionalSeed(seed, nameof(seed));
        References = references is null || references.Count == 0
            ? []
            : Array.AsReadOnly(references.ToArray());
        ReferenceLimits = effectiveLimits;
        Model = AiRequestLimits.ValidateOptionalModel(model, nameof(model));
        IdempotencyKey = AiRequestLimits.ValidateOptionalIdempotencyKey(
            idempotencyKey,
            nameof(idempotencyKey));
    }

    public string Prompt { get; }

    public AiImageAspectRatioId AspectRatio { get; }

    /// <summary>
    /// The background to render, named as the server names it: "transparent"
    /// for a compositing asset, "opaque" for a filled one. Empty leaves the
    /// choice to the model, which is what sending no background means. The
    /// generated file stays PNG whichever is asked for.
    /// </summary>
    public AiImageBackgroundId Background { get; }

    /// <summary>
    /// Repeating a seed with the same prompt reproduces the same picture, which
    /// is what makes iterating on a result possible.
    /// </summary>
    public int? Seed { get; }

    /// <summary>
    /// Existing pictures the generation is guided by, in the order the model
    /// should read them. Up to <see cref="AiRequestLimits.MaxImageReferences"/>,
    /// which is what the operation's price covers, and together no larger than
    /// <see cref="ReferenceLimits"/>; a model that takes fewer says so through
    /// <see cref="AiImageModelCapabilities.MaxReferenceImages"/>.
    /// </summary>
    public IReadOnlyList<AiUploadSource> References { get; }

    /// <summary>
    /// The immutable total byte budget used when this request was validated.
    /// It is copied from the server capability snapshot so a later capability
    /// refresh cannot change the request after construction.
    /// </summary>
    public AiImageReferenceLimits ReferenceLimits { get; }

    /// <summary>
    /// Which model to run on. Null asks for the operation's default; naming one
    /// the server does not offer is refused rather than substituted.
    /// </summary>
    public AiModelId? Model { get; }

    /// <summary>
    /// Names this request, so that sending it again asks the server for the
    /// same one rather than for another.
    /// </summary>
    /// <remarks>
    /// The operation is charged when it is accepted, and the server answers a
    /// repeat of a key it has already seen with the result that key produced —
    /// free, and with a refusal while the first attempt is still running. A
    /// caller retrying after a lost response must send the key it used the
    /// first time or it pays twice for one piece of work; a caller asking for
    /// something new must not, or it is handed the earlier result. Left unset,
    /// each attempt is a new request.
    /// </remarks>
    public string? IdempotencyKey { get; }
}

public sealed record AiImageEditRequest
{
    public AiImageEditRequest(
        AiUploadSource image,
        AiImageEditTaskId task,
        string? prompt = null,
        AiModelId? model = null,
        string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (task.Value.Length == 0)
            throw new ArgumentException("An image edit task is required.", nameof(task));
        Image = image;
        Task = task;
        Prompt = AiRequestLimits.ValidateOptionalPrompt(prompt, nameof(prompt));
        Model = AiRequestLimits.ValidateOptionalModel(model, nameof(model));
        IdempotencyKey = AiRequestLimits.ValidateOptionalIdempotencyKey(
            idempotencyKey,
            nameof(idempotencyKey));
    }

    public AiUploadSource Image { get; }

    public AiImageEditTaskId Task { get; }

    public string? Prompt { get; }

    public AiModelId? Model { get; }

    /// <summary>
    /// Names this request, so that sending it again asks the server for the
    /// same one rather than for another.
    /// </summary>
    /// <remarks>
    /// The operation is charged when it is accepted, and the server answers a
    /// repeat of a key it has already seen with the result that key produced —
    /// free, and with a refusal while the first attempt is still running. A
    /// caller retrying after a lost response must send the key it used the
    /// first time or it pays twice for one piece of work; a caller asking for
    /// something new must not, or it is handed the earlier result. Left unset,
    /// each attempt is a new request.
    /// </remarks>
    public string? IdempotencyKey { get; }
}

public sealed record AiTranscriptionRequest
{
    public AiTranscriptionRequest(
        AiUploadSource audio,
        string? language = null,
        AiModelId? model = null,
        string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.Length > AiRequestLimits.MaxTranscriptionUploadBytes)
            throw new AiFileTooLargeException();

        Audio = audio;
        Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
        Model = AiRequestLimits.ValidateOptionalModel(model, nameof(model));
        IdempotencyKey = AiRequestLimits.ValidateOptionalIdempotencyKey(
            idempotencyKey,
            nameof(idempotencyKey));
    }

    public AiUploadSource Audio { get; }

    public string? Language { get; }

    public AiModelId? Model { get; }

    /// <summary>
    /// Names this request, so that sending it again asks the server for the
    /// same one rather than for another.
    /// </summary>
    /// <remarks>
    /// A transcription is charged when it is accepted, and the server answers a
    /// repeat of a key it has already seen with the result that key produced —
    /// free, and even while the first attempt is still running. A caller that
    /// retries after a lost response, or resumes a run it split into chunks,
    /// must send the key it used the first time or it pays twice for one piece
    /// of audio. Left unset, each attempt is a new request.
    /// </remarks>
    public string? IdempotencyKey { get; }
}

/// <summary>
/// Direction that applies to the whole translation rather than to one segment.
/// A line that does not fit its cue is unreadable however good the wording is,
/// and a series keeps its own names for things.
/// </summary>
public sealed record AiCaptionTranslationStyle
{
    public const int MaxGlossaryEntries = 100;

    public AiCaptionTranslationStyle(
        IReadOnlyDictionary<string, string>? glossary = null,
        int? maxCharactersPerLine = null,
        int? maxLines = null)
    {
        if (glossary is { Count: > MaxGlossaryEntries })
        {
            throw new ArgumentException(
                $"A glossary cannot hold more than {MaxGlossaryEntries} terms.",
                nameof(glossary));
        }

        if (glossary is not null)
        {
            foreach ((string term, string translation) in glossary)
            {
                if (string.IsNullOrEmpty(term) || term.Length > 100)
                    throw new ArgumentException("Glossary terms must be 1 to 100 characters.", nameof(glossary));
                if (string.IsNullOrEmpty(translation) || translation.Length > 200)
                    throw new ArgumentException("Glossary translations must be 1 to 200 characters.", nameof(glossary));
            }
        }

        if (maxCharactersPerLine is not null and (< 1 or > 200))
            throw new ArgumentOutOfRangeException(nameof(maxCharactersPerLine));
        if (maxLines is not null and (< 1 or > 10))
            throw new ArgumentOutOfRangeException(nameof(maxLines));

        Glossary = glossary is null
            ? null
            : new Dictionary<string, string>(glossary).AsReadOnly();
        MaxCharactersPerLine = maxCharactersPerLine;
        MaxLines = maxLines;
    }

    /// <summary>
    /// Term to required translation. These characters count against the same
    /// request budget and the same charge as the subtitle text, because they
    /// reach the provider the same way.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Glossary { get; }

    public int? MaxCharactersPerLine { get; }

    public int? MaxLines { get; }

    public bool IsEmpty
        => (Glossary is null || Glossary.Count == 0)
           && MaxCharactersPerLine is null
           && MaxLines is null;
}

public sealed record AiCaptionTranslationRequest
{
    public AiCaptionTranslationRequest(
        IReadOnlyList<AiCaptionTranslationSegment> segments,
        string targetLanguage,
        string? sourceLanguage = null,
        AiCaptionTranslationStyle? style = null,
        AiModelId? model = null,
        string? idempotencyKey = null,
        AiCaptionTranslationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);
        AiCaptionTranslationLimits effectiveLimits =
            limits ?? AiCaptionTranslationLimits.Default;
        if (segments.Count == 0)
            throw new ArgumentException("At least one subtitle segment is required.", nameof(segments));
        if (segments.Count > effectiveLimits.MaxSegments)
            throw new ArgumentException(
                $"At most {effectiveLimits.MaxSegments} subtitle segments may be translated.",
                nameof(segments));
        if (segments.Any(segment => segment is null))
            throw new ArgumentException("Translation segments cannot contain null.", nameof(segments));

        var ids = new HashSet<string>(StringComparer.Ordinal);
        int characterCount = 0;
        foreach (AiCaptionTranslationSegment segment in segments)
        {
            if (!AiRequestLimits.IsSafeTranslationIdentifier(segment.Id))
                throw new ArgumentException("Translation segment IDs must be 1 to 64 ASCII letters, digits, '_' or '-'.", nameof(segments));
            if (!ids.Add(segment.Id))
                throw new ArgumentException("Translation segment IDs must be unique.", nameof(segments));
            if (string.IsNullOrWhiteSpace(segment.Text)
                || segment.Text.Length > effectiveLimits.MaxCharacters)
            {
                throw new ArgumentException(
                    $"Translation segment text must be 1 to {effectiveLimits.MaxCharacters} characters.",
                    nameof(segments));
            }

            characterCount = checked(characterCount + segment.Text.Length);
            if (segment.Context is { } context
                && !AiRequestLimits.IsSafeTranslationIdentifier(context.GroupId))
            {
                throw new ArgumentException("Translation context group IDs must be 1 to 64 safe ASCII characters.", nameof(segments));
            }
            if (segment.Context is { PartIndex: var partIndex }
                && partIndex >= effectiveLimits.MaxSegments)
            {
                throw new ArgumentException(
                    $"Translation context part indexes must be below {effectiveLimits.MaxSegments}.",
                    nameof(segments));
            }
        }

        Segments = Array.AsReadOnly(segments.ToArray());
        TargetLanguage = targetLanguage.Trim().ToLowerInvariant();
        if (!AiRequestLimits.IsIso6391LanguageCode(TargetLanguage))
            throw new ArgumentException("Target language must be an ISO 639-1 language code.", nameof(targetLanguage));
        if (sourceLanguage is not null && string.IsNullOrWhiteSpace(sourceLanguage))
            throw new ArgumentException(
                "Source language cannot be whitespace.",
                nameof(sourceLanguage));
        SourceLanguage = sourceLanguage?.Trim().ToLowerInvariant();
        if (SourceLanguage is not null && !AiRequestLimits.IsIso6391LanguageCode(SourceLanguage))
            throw new ArgumentException("Source language must be an ISO 639-1 language code.", nameof(sourceLanguage));
        Style = style is null || style.IsEmpty ? null : style;
        if (Style?.Glossary is { } glossary)
        {
            foreach ((string term, string translation) in glossary)
                characterCount = checked(characterCount + term.Length + translation.Length);
        }
        if (characterCount > effectiveLimits.MaxCharacters)
            throw new ArgumentException(
                $"Translation text cannot exceed {effectiveLimits.MaxCharacters} characters.",
                nameof(segments));
        Model = AiRequestLimits.ValidateOptionalModel(model, nameof(model));
        IdempotencyKey = AiRequestLimits.ValidateOptionalIdempotencyKey(
            idempotencyKey,
            nameof(idempotencyKey));
        Limits = effectiveLimits;
        _ = AiCaptionTranslationRequestTransport.CreatePayload(this);
    }

    public IReadOnlyList<AiCaptionTranslationSegment> Segments { get; }

    public string TargetLanguage { get; }

    public string? SourceLanguage { get; }

    public AiCaptionTranslationStyle? Style { get; }

    public AiModelId? Model { get; }

    public AiCaptionTranslationLimits Limits { get; }

    /// <summary>
    /// Names this request, so that sending it again asks the server for the
    /// same one rather than for another.
    /// </summary>
    /// <remarks>
    /// The operation is charged when it is accepted, and the server answers a
    /// repeat of a key it has already seen with the result that key produced —
    /// free, and with a refusal while the first attempt is still running. A
    /// caller retrying after a lost response must send the key it used the
    /// first time or it pays twice for one piece of work; a caller asking for
    /// something new must not, or it is handed the earlier result. Left unset,
    /// each attempt is a new request.
    /// </remarks>
    public string? IdempotencyKey { get; }
}

public sealed record AiVideoGenerationRequest
{
    public AiVideoGenerationRequest(
        string prompt,
        int durationSeconds,
        AiVideoResolutionId resolution,
        AiVideoAspectRatioId aspectRatio,
        bool generateAudio = true,
        int? seed = null,
        AiUploadSource? firstFrame = null,
        AiUploadSource? lastFrame = null,
        AiModelId? model = null,
        string? idempotencyKey = null)
    {
        AiRequestLimits.ValidateVideoDurationSeconds(durationSeconds, nameof(durationSeconds));
        if (resolution.Value.Length == 0)
            throw new ArgumentException("A video resolution is required.", nameof(resolution));
        if (aspectRatio.Value.Length == 0)
            throw new ArgumentException("A video aspect ratio is required.", nameof(aspectRatio));
        if (lastFrame is not null && firstFrame is null)
            throw new ArgumentException("A last frame requires a first frame.", nameof(lastFrame));
        if (firstFrame?.Length > AiRequestLimits.MaxFrameUploadBytes)
            throw new AiFileTooLargeException();
        if (lastFrame?.Length > AiRequestLimits.MaxFrameUploadBytes)
            throw new AiFileTooLargeException();

        Prompt = AiRequestLimits.ValidatePrompt(prompt, nameof(prompt));
        DurationSeconds = durationSeconds;
        Resolution = resolution;
        AspectRatio = aspectRatio;
        GenerateAudio = generateAudio;
        Seed = AiRequestLimits.ValidateOptionalSeed(seed, nameof(seed));
        FirstFrame = firstFrame;
        LastFrame = lastFrame;
        Model = AiRequestLimits.ValidateOptionalModel(model, nameof(model));
        IdempotencyKey = AiRequestLimits.ValidateOptionalIdempotencyKey(
            idempotencyKey,
            nameof(idempotencyKey));
    }

    public string Prompt { get; }

    public int DurationSeconds { get; }

    /// <summary>How many pixels; <see cref="AspectRatio"/> says what shape they are in.</summary>
    public AiVideoResolutionId Resolution { get; }

    public AiVideoAspectRatioId AspectRatio { get; }

    /// <summary>
    /// The model generates sound. Leaving it on matches what the plan is priced
    /// for; turning it off is for a clip that will carry its own audio.
    /// </summary>
    public bool GenerateAudio { get; }

    public int? Seed { get; }

    public AiUploadSource? FirstFrame { get; }

    public AiUploadSource? LastFrame { get; }

    public AiModelId? Model { get; }

    /// <summary>
    /// Names this request, so that sending it again asks the server for the
    /// same one rather than for another.
    /// </summary>
    /// <remarks>
    /// The operation is charged when it is accepted, and the server answers a
    /// repeat of a key it has already seen with the result that key produced —
    /// free, and with a refusal while the first attempt is still running. A
    /// caller retrying after a lost response must send the key it used the
    /// first time or it pays twice for one piece of work; a caller asking for
    /// something new must not, or it is handed the earlier result. Left unset,
    /// each attempt is a new request.
    /// </remarks>
    public string? IdempotencyKey { get; }
}

public sealed record AiJobPageRequest
{
    public AiJobPageRequest(string? cursor = null, int limit = 50)
    {
        if (limit is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));
        Cursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor;
        Limit = limit;
    }

    public string? Cursor { get; }

    public int Limit { get; }
}

// Usage is expressed as a proportion. The server owns the unit accounting so the
// per-operation cost never reaches the client.
public sealed record AiMonthlyUsage(int UsedPercent, int RemainingPercent, bool IsExhausted);

public sealed record AiBalance(
    AiMonthlyUsage MonthlyUsage,
    int AdditionalCredits,
    bool HasAdditionalCreditDebt);

/// <summary>
/// What the server has said about starting an operation. "Not answered" is a
/// state of its own: a server that never mentioned an operation has not refused
/// it, and reporting that as a refusal sends the account to buy credits it
/// already has.
/// </summary>
public enum AiOperationAvailabilityState
{
    Unknown,
    Available,
    Unavailable,
}

// Which operations the server will accept right now, keyed by operation id.
public sealed class AiOperationAvailability
{
    public AiOperationAvailability(IEnumerable<KeyValuePair<AiOperationId, bool>> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        Operations = operations.ToImmutableDictionary(pair => pair.Key, pair => pair.Value);
    }

    public ImmutableDictionary<AiOperationId, bool> Operations { get; }

    /// <summary>
    /// An operation the server did not report reads as
    /// <see cref="AiOperationAvailabilityState.Unknown"/>, mirroring
    /// <see cref="AiModelAvailability.CanStart"/>: silence is not a refusal.
    /// </summary>
    public AiOperationAvailabilityState GetState(AiOperationId operation)
    {
        if (!Operations.TryGetValue(operation, out bool allowed))
            return AiOperationAvailabilityState.Unknown;
        return allowed
            ? AiOperationAvailabilityState.Available
            : AiOperationAvailabilityState.Unavailable;
    }
}

/// <summary>
/// Which of an operation's models the account can pay for right now. An
/// operation reads as available when any one of them does, so a picker needs
/// this to know which entries to offer.
/// </summary>
public sealed class AiModelAvailability
{
    public static AiModelAvailability Empty { get; } =
        new(ImmutableDictionary<AiOperationId, ImmutableDictionary<AiModelId, bool>>.Empty);

    public AiModelAvailability(
        IEnumerable<KeyValuePair<AiOperationId, ImmutableDictionary<AiModelId, bool>>> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        Operations = operations.ToImmutableDictionary(pair => pair.Key, pair => pair.Value);
    }

    public ImmutableDictionary<AiOperationId, ImmutableDictionary<AiModelId, bool>> Operations { get; }

    /// <summary>
    /// Whether that model can be started. An operation the server did not
    /// report says nothing about its models, so the answer falls back to the
    /// operation-wide flag its caller already has.
    /// </summary>
    public bool CanStart(AiOperationId operation, AiModelId model, bool fallback)
        => Operations.TryGetValue(operation, out ImmutableDictionary<AiModelId, bool>? models)
           && models.TryGetValue(model, out bool allowed)
            ? allowed
            : fallback;
}

public sealed record AiEntitlements(
    string? Plan,
    string? SubscriptionStatus,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    bool CanUseAi,
    AiBalance Balance,
    AiOperationAvailability Availability)
{
    /// <summary>
    /// Empty against a server that predates per-model pricing, which is the
    /// same as saying nothing about any particular model.
    /// </summary>
    public AiModelAvailability ModelAvailability { get; init; } = AiModelAvailability.Empty;
}

public sealed record AiImageResult(
    AiJobId? JobId,
    AiContentId FileId,
    Uri ContentUri,
    AiContentMetadata? ContentMetadata = null);

public sealed record AiVideoGenerationResult(
    AiJobId JobId,
    AiJobStatusId Status);

public sealed record AiVideoJob(
    AiJobId JobId,
    AiJobStatusId Status,
    AiContentId? FileId,
    Uri? ContentUri,
    string? Error,
    AiContentMetadata? ContentMetadata = null);

public sealed record AiContentMetadata
{
    public AiContentMetadata(string? fileName, string? contentType)
    {
        FileName = AiContentMetadataValidator.NormalizeFileName(fileName);
        ContentType = AiContentMetadataValidator.NormalizeContentType(contentType);
    }

    public string? FileName { get; }

    public string? ContentType { get; }

    public static AiContentMetadata? Combine(
        AiContentMetadata? declared,
        AiContentMetadata? downloaded)
    {
        if (declared is null)
            return downloaded;
        if (downloaded is null)
            return declared;

        if (declared.FileName is not null
            && downloaded.FileName is not null
            && !StringComparer.Ordinal.Equals(declared.FileName, downloaded.FileName))
        {
            throw new AiException("The downloaded AI content filename does not match its job metadata.");
        }

        if (declared.ContentType is not null
            && downloaded.ContentType is not null
            && !StringComparer.OrdinalIgnoreCase.Equals(declared.ContentType, downloaded.ContentType))
        {
            throw new AiException("The downloaded AI content type does not match its job metadata.");
        }

        return new AiContentMetadata(
            downloaded.FileName ?? declared.FileName,
            downloaded.ContentType ?? declared.ContentType);
    }

    public string GetFileExtension(string fallbackExtension, string requiredMediaKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredMediaKind);
        string normalizedFallback = NormalizeExtension(fallbackExtension);
        string? contentTypeExtension = ContentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "video/quicktime" => ".mov",
            "video/x-matroska" => ".mkv",
            "audio/wav" or "audio/x-wav" => ".wav",
            "audio/mpeg" => ".mp3",
            "audio/flac" => ".flac",
            "audio/mp4" => ".m4a",
            "audio/ogg" => ".ogg",
            "audio/webm" => ".webm",
            _ => null,
        };
        if (ContentType is not null && contentTypeExtension is null)
            throw new AiException("The AI content type is unsupported.");

        string? fileNameExtension = string.IsNullOrWhiteSpace(FileName)
            ? null
            : NormalizeExtension(Path.GetExtension(FileName));

        if (contentTypeExtension is not null
            && fileNameExtension is not null
            && !StringComparer.OrdinalIgnoreCase.Equals(contentTypeExtension, fileNameExtension)
            && !(contentTypeExtension == ".jpg" && fileNameExtension == ".jpeg"))
        {
            throw new AiException("The AI content filename and content type describe different formats.");
        }

        string result = contentTypeExtension ?? fileNameExtension ?? normalizedFallback;
        bool mediaKindMatches = requiredMediaKind switch
        {
            "image" => result is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif",
            "video" => result is ".mp4" or ".webm" or ".mov" or ".mkv",
            "audio" => result is ".wav" or ".mp3" or ".flac" or ".m4a" or ".ogg" or ".webm",
            _ => throw new ArgumentException("The required media kind is invalid.", nameof(requiredMediaKind)),
        };
        if (!mediaKindMatches)
            throw new AiException($"The AI content is not valid {requiredMediaKind} media.");
        return result;
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        string normalized = extension.StartsWith('.') ? extension : $".{extension}";
        if (normalized.Length is < 2 or > 11
            || normalized.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("The file extension is invalid.", nameof(extension));
        }

        normalized = normalized.ToLowerInvariant();
        if (normalized is not (
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif"
            or ".mp4" or ".webm" or ".mov" or ".mkv"
            or ".wav" or ".mp3" or ".flac" or ".m4a" or ".ogg"))
        {
            throw new AiException("The AI content uses an unsupported file format.");
        }

        return normalized;
    }
}

public sealed record AiContentDownload(AiContentMetadata? Metadata);

public sealed record AiTranscriptionResponse(
    AiJobId? JobId,
    AiTranscriptionSegment[] Segments,
    string? Language,
    AiTranscriptionWord[]? Words);

public sealed class AiTranscriptionWord
{
    public required double Start { get; init; }

    public required double End { get; init; }

    public required string Word { get; init; }
}

public sealed class AiTranscriptionSegment
{
    public required double Start { get; init; }

    public required double End { get; init; }

    public required string Text { get; init; }
}

public sealed record AiCaptionTranslationSegment
{
    public required string Id { get; init; }

    public required string Text { get; init; }

    public AiCaptionTranslationSegmentContext? Context { get; init; }
}

public sealed record AiCaptionTranslationSegmentContext
{
    public AiCaptionTranslationSegmentContext(
        string groupId,
        int partIndex,
        TimeSpan start,
        TimeSpan end)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        if (!AiRequestLimits.IsSafeTranslationIdentifier(groupId))
            throw new ArgumentException(
                "Translation context group IDs must be 1 to 64 safe ASCII characters.",
                nameof(groupId));
        if (partIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(partIndex));
        if (start < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (end <= start)
            throw new ArgumentOutOfRangeException(nameof(end));

        GroupId = groupId.Trim();
        PartIndex = partIndex;
        Start = start;
        End = end;
    }

    public string GroupId { get; }

    public int PartIndex { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }
}

/// <summary>
/// A rough version of a picture, sent while the finished one is still being
/// worked out. The bytes are a whole image of their own and can be shown as
/// they are.
/// </summary>
public sealed record AiImagePreview(int Index, ReadOnlyMemory<byte> Bytes);

public sealed record AiCaptionTranslationResponse(
    AiJobId? JobId,
    AiCaptionTranslationSegment[] Segments);

public sealed record AiJobPage(
    ImmutableArray<AiJob> Jobs,
    string? NextCursor);

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
    public static AiModelCatalog ToModel(AiCapabilitiesResponse response)
    {
        if (response.Operations is null || response.Operations.Count == 0)
            return AiModelCatalog.Empty;

        return new AiModelCatalog(
            response.Operations
                .Select(pair => new KeyValuePair<AiOperationId, ImmutableArray<AiModelOption>>(
                    new AiOperationId(pair.Key),
                    ToModelOptions(new AiOperationId(pair.Key), pair.Value)))
                .Where(pair => !pair.Value.IsDefaultOrEmpty),
            // A models list the server sent and left empty. A server that sends
            // none at all is another thing entirely, and stays absent here.
            response.Operations
                .Where(pair => pair.Value.Models is { IsDefaultOrEmpty: true })
                .Select(pair => new AiOperationId(pair.Key)))
        {
            ImageReferenceLimits = ToImageReferenceLimits(response),
            CaptionTranslationLimits = ToCaptionTranslationLimits(response),
        };
    }

    private static AiImageReferenceLimits ToImageReferenceLimits(
        AiCapabilitiesResponse response)
        => response.Operations.TryGetValue(
                AiOperations.ImageGeneration.Value,
                out AiOperationCapabilityResponse? operation)
            && operation.MaxReferenceImagesTotalBytes is > 0 and var maxTotalBytes
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
            NarrowDimension(
                model.AspectRatios is { } aspectRatios
                    ? AiCapabilityDimension<string>.Supported(
                        aspectRatios.Where(value => !string.IsNullOrWhiteSpace(value)))
                    : AiCapabilityDimension<string>.Unspecified,
                capability.AspectRatios),
            NarrowDimension(
                model.Backgrounds is { } backgrounds
                    ? AiCapabilityDimension<string>.Supported(
                        backgrounds.Where(value => !string.IsNullOrWhiteSpace(value)))
                    : AiCapabilityDimension<string>.Unspecified,
                capability.Backgrounds),
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
                model.Resolutions is { } resolutions
                    ? AiCapabilityDimension<string>.Supported(
                        resolutions.Where(value => !string.IsNullOrWhiteSpace(value)))
                    : AiCapabilityDimension<string>.Unspecified,
                model.AspectRatios is { } aspectRatios
                    ? AiCapabilityDimension<string>.Supported(
                        aspectRatios.Where(value => !string.IsNullOrWhiteSpace(value)))
                    : AiCapabilityDimension<string>.Unspecified,
                model.Audio ?? true,
                model.Seed ?? true,
                model.FirstFrame ?? true,
                model.LastFrame ?? true),
            capability);
    }

    private static ImmutableArray<AiModelOption> ToModelOptions(
        AiOperationId operation,
        AiOperationCapabilityResponse capability)
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
                operation == AiOperations.VideoGeneration
                    ? ToVideoCapabilities(model, capability)
                    : null,
                operation == AiOperations.ImageGeneration
                    || operation.Value.StartsWith("image.edit.", StringComparison.Ordinal)
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
        AiCapabilityDimension<int> durations = !model.DurationsSeconds.IsSpecified
            ? model.DurationsSeconds
            : AiCapabilityDimension<int>.Supported(model.DurationsSeconds.Values.Where(seconds =>
                seconds >= (capability.MinDurationSeconds ?? int.MinValue)
                && seconds <= (capability.MaxDurationSeconds ?? int.MaxValue)));
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

internal static class AiContentMetadataValidator
{
    public static string? NormalizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        string normalized = fileName.Trim();
        if (normalized.Length > 255
            || normalized.Contains('/')
            || normalized.Contains('\\')
            || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || Path.IsPathRooted(normalized)
            || Path.GetFileName(normalized) != normalized
            || normalized is "." or "..")
        {
            throw new AiException("The AI response contains an invalid content filename.");
        }

        return normalized;
    }

    public static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;
        if (!System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(
                contentType.Trim(),
                out System.Net.Http.Headers.MediaTypeHeaderValue? parsed))
        {
            throw new AiException("The AI response contains an invalid content type.");
        }

        return parsed.MediaType;
    }
}

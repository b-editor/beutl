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
/// What one video model will take. Published per model because it differs per
/// model: MiniMax H3 renders only at 2K and refuses anything under five
/// seconds, while Veo 3.1 takes 4, 6 or 8 seconds at 720p or 1080p. An empty
/// list is the server saying nothing about that dimension, which leaves the
/// dialog offering everything it knows how to ask for.
/// </summary>
public sealed record AiVideoModelCapabilities(
    ImmutableArray<int> DurationsSeconds,
    ImmutableArray<string> Resolutions,
    ImmutableArray<string> AspectRatios,
    bool SupportsAudio,
    bool SupportsSeed)
{
    public static AiVideoModelCapabilities Unrestricted { get; } =
        new([], [], [], true, true);

    /// <summary>
    /// False for a model that shares no resolution or shape with what the
    /// server accepts. The lists are already narrowed to that when they are
    /// read, so nothing left on one of them means every request naming this
    /// model would be refused, and offering it is worse than hiding it.
    /// </summary>
    public bool CanServeAnything()
        => !Resolutions.IsDefaultOrEmpty && !AspectRatios.IsDefaultOrEmpty;
}

/// <summary>
/// What one image model will take. GPT Image-1 renders 1:1, 3:2 and 2:3 and
/// refuses everything else; the backgrounds differ per model as well, and only
/// some take a seed or accept a picture to work from — which every edit
/// depends on.
/// </summary>
public sealed record AiImageModelCapabilities(
    ImmutableArray<string> AspectRatios,
    ImmutableArray<string> Backgrounds,
    bool SupportsSeed,
    int MaxReferenceImages)
{
    public static AiImageModelCapabilities Unrestricted { get; } =
        new([], [], true, AiRequestLimits.MaxImageReferences);

    /// <summary>
    /// False for a model that shares no shape with what the server accepts, or
    /// that cannot be handed the picture an edit is made of.
    /// </summary>
    public bool CanServeAnything(bool requiresReferenceImages)
        => !AspectRatios.IsDefaultOrEmpty
           && (!requiresReferenceImages || MaxReferenceImages > 0);
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
        IEnumerable<KeyValuePair<AiOperationId, ImmutableArray<AiModelOption>>> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        Operations = operations.ToImmutableDictionary(pair => pair.Key, pair => pair.Value);
    }

    public ImmutableDictionary<AiOperationId, ImmutableArray<AiModelOption>> Operations { get; }

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
        public Translation(int characterCount, AiModelId? model = null)
            : base(AiOperations.CaptionTranslation, model)
        {
            if (characterCount is <= 0 or > 20_000)
                throw new ArgumentOutOfRangeException(nameof(characterCount));
            CharacterCount = characterCount;
        }

        public int CharacterCount { get; }
    }
}

public static class AiRequestLimits
{
    public const int MaxPromptLength = 4_000;

    public const long MaxFrameUploadBytes = 5L * 1024 * 1024;

    public const long MaxImageUploadBytes = 20L * 1024 * 1024;

    // What the server is priced for. Each model publishes its own count and the
    // smaller of the two is what may be sent; this client sends one picture
    // today, and the number is what a server that publishes nothing is read as.
    public const int MaxImageReferences = 4;

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

    internal static string? ValidateOptionalPrompt(string? prompt, string parameterName)
        => string.IsNullOrWhiteSpace(prompt) ? null : ValidatePrompt(prompt, parameterName);

    internal static int? ValidateOptionalSeed(int? seed, string parameterName)
    {
        if (seed is null) return null;
        if (seed.Value is < MinSeed or > MaxSeed)
            throw new ArgumentOutOfRangeException(parameterName);
        return seed;
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
        AiUploadSource? reference = null,
        AiModelId? model = null)
    {
        if (aspectRatio.Value.Length == 0)
            throw new ArgumentException("An image aspect ratio is required.", nameof(aspectRatio));
        if (reference?.Length > AiRequestLimits.MaxImageUploadBytes)
            throw new AiFileTooLargeException();

        Prompt = AiRequestLimits.ValidatePrompt(prompt, nameof(prompt));
        AspectRatio = aspectRatio;
        Background = background;
        Seed = AiRequestLimits.ValidateOptionalSeed(seed, nameof(seed));
        Reference = reference;
        Model = AiRequestLimits.ValidateOptionalModel(model, nameof(model));
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
    /// An existing picture the generation is guided by. One at most: that is
    /// what the operation's price covers.
    /// </summary>
    public AiUploadSource? Reference { get; }

    /// <summary>
    /// Which model to run on. Null asks for the operation's default; naming one
    /// the server does not offer is refused rather than substituted.
    /// </summary>
    public AiModelId? Model { get; }
}

public sealed record AiImageEditRequest
{
    public AiImageEditRequest(
        AiUploadSource image,
        AiImageEditTaskId task,
        string? prompt = null,
        AiModelId? model = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (task.Value.Length == 0)
            throw new ArgumentException("An image edit task is required.", nameof(task));
        Image = image;
        Task = task;
        Prompt = AiRequestLimits.ValidateOptionalPrompt(prompt, nameof(prompt));
        Model = AiRequestLimits.ValidateOptionalModel(model, nameof(model));
    }

    public AiUploadSource Image { get; }

    public AiImageEditTaskId Task { get; }

    public string? Prompt { get; }

    public AiModelId? Model { get; }
}

public sealed record AiTranscriptionRequest
{
    public AiTranscriptionRequest(
        AiUploadSource audio,
        string? language = null,
        AiModelId? model = null)
    {
        ArgumentNullException.ThrowIfNull(audio);
        Audio = audio;
        Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
        Model = AiRequestLimits.ValidateOptionalModel(model, nameof(model));
    }

    public AiUploadSource Audio { get; }

    public string? Language { get; }

    public AiModelId? Model { get; }
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
        AiModelId? model = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);
        if (segments.Count == 0)
            throw new ArgumentException("At least one subtitle segment is required.", nameof(segments));
        if (segments.Any(segment => segment is null))
            throw new ArgumentException("Translation segments cannot contain null.", nameof(segments));

        Segments = Array.AsReadOnly(segments.ToArray());
        TargetLanguage = targetLanguage.Trim().ToLowerInvariant();
        SourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage)
            ? null
            : sourceLanguage.Trim().ToLowerInvariant();
        Style = style is null || style.IsEmpty ? null : style;
        Model = AiRequestLimits.ValidateOptionalModel(model, nameof(model));
    }

    public IReadOnlyList<AiCaptionTranslationSegment> Segments { get; }

    public string TargetLanguage { get; }

    public string? SourceLanguage { get; }

    public AiCaptionTranslationStyle? Style { get; }

    public AiModelId? Model { get; }
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
        AiModelId? model = null)
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
                    ToModelOptions(pair.Value)))
                .Where(pair => !pair.Value.IsDefaultOrEmpty));
    }

    // Null for every operation but image, where the server publishes nothing of
    // the sort. The ratios are narrowed to the operation's own list, so a shape
    // the server would refuse never reaches a dialog.
    private static AiImageModelCapabilities? ToImageCapabilities(
        AiModelDescriptionResponse model,
        AiOperationCapabilityResponse capability)
    {
        if (model.AspectRatios is null
            && model.Backgrounds is null
            && model.MaxReferenceImages is null)
        {
            return null;
        }

        return new AiImageModelCapabilities(
            NarrowToOperation(
                model.AspectRatios is { } aspectRatios
                    ? [.. aspectRatios.Where(value => !string.IsNullOrWhiteSpace(value))]
                    : [],
                capability.AspectRatios),
            NarrowToOperation(
                model.Backgrounds is { } backgrounds
                    ? [.. backgrounds.Where(value => !string.IsNullOrWhiteSpace(value))]
                    : [],
                capability.Backgrounds),
            model.Seed ?? true,
            model.MaxReferenceImages ?? AiRequestLimits.MaxImageReferences);
    }

    // Null for every operation but video, where the server publishes nothing of
    // the sort. A video model that publishes none of the five reads the same as
    // one this client asked about before they existed: unrestricted.
    private static AiVideoModelCapabilities? ToVideoCapabilities(
        AiModelDescriptionResponse model,
        AiOperationCapabilityResponse capability)
    {
        if (model.DurationsSeconds is null
            && model.Resolutions is null
            && model.AspectRatios is null
            && model.Audio is null
            && model.Seed is null)
        {
            return null;
        }

        return Narrow(
            new AiVideoModelCapabilities(
                model.DurationsSeconds ?? [],
                model.Resolutions is { } resolutions
                    ? [.. resolutions.Where(value => !string.IsNullOrWhiteSpace(value))]
                    : [],
                model.AspectRatios is { } aspectRatios
                    ? [.. aspectRatios.Where(value => !string.IsNullOrWhiteSpace(value))]
                    : [],
                model.Audio ?? true,
                model.Seed ?? true),
            capability);
    }

    private static ImmutableArray<AiModelOption> ToModelOptions(
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
                ToVideoCapabilities(model, capability),
                ToImageCapabilities(model, capability)))
            .ToImmutableArray();
    }

    // A model's own lists narrowed to what the operation accepts, so a shape
    // the server would refuse never reaches the dialog. Order follows the
    // operation's, which runs from the smallest resolution upwards.
    private static AiVideoModelCapabilities Narrow(
        AiVideoModelCapabilities model,
        AiOperationCapabilityResponse capability)
    {
        ImmutableArray<int> durations = model.DurationsSeconds.IsDefaultOrEmpty
            ? []
            : [.. model.DurationsSeconds.Where(seconds =>
                seconds >= (capability.MinDurationSeconds ?? int.MinValue)
                && seconds <= (capability.MaxDurationSeconds ?? int.MaxValue))];
        return model with
        {
            DurationsSeconds = durations,
            Resolutions = NarrowToOperation(model.Resolutions, capability.Resolutions),
            AspectRatios = NarrowToOperation(model.AspectRatios, capability.AspectRatios),
        };
    }

    // A model that publishes nothing takes whatever the operation accepts, so
    // what comes back is always the list to offer rather than a hint that one
    // has to be guessed at. Empty then means the two share nothing.
    private static ImmutableArray<string> NarrowToOperation(
        ImmutableArray<string> model,
        ImmutableArray<string>? operation)
    {
        if (operation is not { IsDefaultOrEmpty: false } offered)
            return model;
        if (model.IsDefaultOrEmpty)
            return offered;
        return [.. offered.Where(model.Contains)];
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

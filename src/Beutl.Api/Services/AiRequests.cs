using System.Collections.Immutable;

namespace Beutl.Api.Services;

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
        /// <summary>
        /// Creates an availability check for an operation whose cost has no variable quantity.
        /// Custom operation identifiers are forwarded unchanged to the server.
        /// </summary>
        public Fixed(AiOperationId operation, AiModelId? model = null)
            : base(operation, model)
        {
        }
    }

    public sealed record Video : AiOperationAvailabilityRequest
    {
        /// <summary>
        /// Creates a duration-based video availability check for a built-in or custom operation.
        /// </summary>
        public Video(
            AiOperationId operation,
            int durationSeconds,
            AiModelId? model = null)
            : base(operation, model)
        {
            DurationSeconds = AiRequestLimits.ValidateVideoDurationSeconds(
                durationSeconds,
                nameof(durationSeconds));
        }

        public int DurationSeconds { get; }
    }

    public sealed record Transcription : AiOperationAvailabilityRequest
    {
        /// <summary>
        /// Creates a duration-based transcription availability check for a built-in or custom operation.
        /// </summary>
        public Transcription(
            AiOperationId operation,
            double durationSeconds,
            AiModelId? model = null)
            : base(operation, model)
        {
            if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            DurationSeconds = durationSeconds;
        }

        public double DurationSeconds { get; }
    }

    public sealed record Translation : AiOperationAvailabilityRequest
    {
        /// <summary>
        /// Creates a character-count-based translation availability check for a built-in or custom operation.
        /// </summary>
        public Translation(
            AiOperationId operation,
            int characterCount,
            AiModelId? model = null,
            AiCaptionTranslationLimits? limits = null)
            : base(operation, model)
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
        string? idempotencyKey = null,
        IReadOnlyList<AiUploadSource>? inputReferences = null)
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

        InputReferences = inputReferences?.ToImmutableArray() ?? [];
        AiVideoInputLimits.ValidateReferences(InputReferences);
        if (InputReferences.Count > 0 && (firstFrame is not null || lastFrame is not null))
            throw new ArgumentException("Frames and references cannot be combined.", nameof(inputReferences));
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

    public IReadOnlyList<AiUploadSource> InputReferences { get; }

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

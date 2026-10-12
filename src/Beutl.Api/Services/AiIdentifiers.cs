namespace Beutl.Api.Services;

public readonly record struct AiOperationId
{
    private readonly string? _value;

    public AiOperationId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public override string ToString() => Value;
}

/// <summary>
/// One of the models an operation may run on, as registered on the server. The
/// list is not known at build time, so it is never a fixed set here.
/// </summary>
public readonly record struct AiModelId
{
    private readonly string? _value;

    public AiModelId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public override string ToString() => Value;
}

public static class AiOperations
{
    public static AiOperationId ImageGeneration { get; } = new("image.generate");

    public static AiOperationId VideoGeneration { get; } = new("video.generate");
    public static AiOperationId VideoEditing { get; } = new("video.edit");
    public static AiOperationId VideoExtension { get; } = new("video.extend");
    public static AiOperationId VideoMotion { get; } = new("video.motion");

    public static AiOperationId Transcription { get; } = new("audio.transcribe");

    public static AiOperationId CaptionTranslation { get; } = new("subtitle.translate");

    public static AiOperationId ImageEdit(AiImageEditTaskId task)
        => new($"image.edit.{task.Value}");
}

// The server is asked for a shape, not a pixel count: "16:9" and "9:16" are the
// ones a video editor needs and no fixed size could express them.
public readonly record struct AiImageAspectRatioId
{
    private readonly string? _value;

    public AiImageAspectRatioId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public override string ToString() => Value;
}

// Named rather than a flag: the server publishes which backgrounds each model
// takes, and a model that fills a background in is not the same as one that
// cuts it out.
public readonly record struct AiImageBackgroundId
{
    private readonly string? _value;

    public AiImageBackgroundId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public override string ToString() => Value;
}

public readonly record struct AiVideoAspectRatioId
{
    private readonly string? _value;

    public AiVideoAspectRatioId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public override string ToString() => Value;
}

public readonly record struct AiImageEditTaskId
{
    private readonly string? _value;

    public AiImageEditTaskId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public override string ToString() => Value;
}

public readonly record struct AiVideoResolutionId
{
    private readonly string? _value;

    public AiVideoResolutionId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public override string ToString() => Value;
}

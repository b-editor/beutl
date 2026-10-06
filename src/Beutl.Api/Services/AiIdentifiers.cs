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

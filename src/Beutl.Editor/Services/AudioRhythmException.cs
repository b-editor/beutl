namespace Beutl.Editor.Services;

internal enum AudioRhythmError
{
    MediaNotFound,
    ValidationRejected,
    CodecUnavailable
}

// The estimator reports why it stopped in terms the agent toolkit maps to its tool errors; the editor itself has
// no media error codes to throw.
internal sealed class AudioRhythmException(
    AudioRhythmError kind, string message, string? target = null, string? hint = null)
    : Exception(message)
{
    public AudioRhythmError Kind { get; } = kind;

    public string? Target { get; } = target;

    public string? Hint { get; } = hint;
}

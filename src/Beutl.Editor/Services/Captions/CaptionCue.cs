namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Represents one provider-independent caption cue.
/// </summary>
public sealed record CaptionCue
{
    private string _text = string.Empty;
    private CaptionMetadata _metadata = CaptionMetadata.Empty;

    public CaptionCue(
        TimeSpan start,
        TimeSpan end,
        string text,
        string? speaker = null,
        string? language = null,
        CaptionMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        Start = start;
        End = end;
        Text = text;
        Speaker = speaker;
        Language = language;
        Metadata = metadata ?? CaptionMetadata.Empty;
    }

    public TimeSpan Start { get; init; }

    public TimeSpan End { get; init; }

    public string Text
    {
        get => _text;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _text = value;
        }
    }

    public string? Speaker { get; init; }

    public string? Language { get; init; }

    public CaptionMetadata Metadata
    {
        get => _metadata;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _metadata = value;
        }
    }
}

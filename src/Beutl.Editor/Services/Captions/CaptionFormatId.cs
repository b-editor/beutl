namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Identifies a caption format without restricting codecs to a closed set of values.
/// </summary>
public readonly struct CaptionFormatId : IEquatable<CaptionFormatId>
{
    private readonly string? _value;

    public CaptionFormatId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value.Trim();
    }

    public string Value => _value ?? string.Empty;

    public bool Equals(CaptionFormatId other)
        => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is CaptionFormatId other && Equals(other);

    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(CaptionFormatId left, CaptionFormatId right) => left.Equals(right);

    public static bool operator !=(CaptionFormatId left, CaptionFormatId right) => !left.Equals(right);

}

/// <summary>
/// Well-known format identifiers supplied by the built-in codecs.
/// </summary>
public static class CaptionFormats
{
    public static CaptionFormatId Srt { get; } = new("srt");

    public static CaptionFormatId WebVtt { get; } = new("webvtt");

    public static CaptionFormatId Ass { get; } = new("ass");
}

namespace BeUtl.Media;

public readonly struct FontMetrics : IEquatable<FontMetrics>
{
    public float Leading { get; init; }

    public float CapHeight { get; init; }

    public float XHeight { get; init; }

    public float XMax { get; init; }

    public float XMin { get; init; }

    public float MaxCharacterWidth { get; init; }

    public float AverageCharacterWidth { get; init; }

    public float Bottom { get; init; }

    public float Descent { get; init; }

    public float Ascent { get; init; }

    public float Top { get; init; }

    public override bool Equals(object? obj)
    {
        return obj is FontMetrics metrics && Equals(metrics);
    }

    public bool Equals(FontMetrics other)
    {
        return Leading == other.Leading && CapHeight == other.CapHeight && XHeight == other.XHeight && XMax == other.XMax && XMin == other.XMin && MaxCharacterWidth == other.MaxCharacterWidth && AverageCharacterWidth == other.AverageCharacterWidth && Bottom == other.Bottom && Descent == other.Descent && Ascent == other.Ascent && Top == other.Top;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Leading);
        hash.Add(CapHeight);
        hash.Add(XHeight);
        hash.Add(XMax);
        hash.Add(XMin);
        hash.Add(MaxCharacterWidth);
        hash.Add(AverageCharacterWidth);
        hash.Add(Bottom);
        hash.Add(Descent);
        hash.Add(Ascent);
        hash.Add(Top);
        return hash.ToHashCode();
    }

    public static bool operator ==(FontMetrics left, FontMetrics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FontMetrics left, FontMetrics right)
    {
        return !(left == right);
    }
}

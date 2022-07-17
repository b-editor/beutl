namespace BeUtl.Media.TextFormatting;

public readonly record struct StringSpan(string Source, int Start, int Length)
{
    public static StringSpan Empty => new(string.Empty, 0, 0);

    public override string ToString()
    {
        return AsSpan().ToString();
    }

    public ReadOnlySpan<char> AsSpan()
    {
        return IsValid() ? Source.AsSpan(Start, Length) : default;
    }

    public StringSpan Slice(int start, int length)
    {
        var r = new StringSpan(Source, Start + start, length);
        return r;
    }

    public StringSpan Slice(int start)
    {
        var r = new StringSpan(Source, Start + start, Length - start);
        return r;
    }

    public bool IsValid()
    {
        return Start >= 0 && (Start + Length) <= Source.Length;
    }
}

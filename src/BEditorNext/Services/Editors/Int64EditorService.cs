namespace BEditorNext.Services.Editors;

public sealed class Int64EditorService : INumberEditorService<long>
{
    public long Clamp(long value, long min, long max)
    {
        return Math.Clamp(value, min, max);
    }

    public long Decrement(long value, int increment)
    {
        return value - increment;
    }

    public long Increment(long value, int increment)
    {
        return value + increment;
    }

    public bool TryParse(string? s, out long result)
    {
        return long.TryParse(s, out result);
    }
}

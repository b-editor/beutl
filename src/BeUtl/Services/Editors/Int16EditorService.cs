namespace BeUtl.Services.Editors;

public sealed class Int16EditorService : INumberEditorService<short>
{
    public short Clamp(short value, short min, short max)
    {
        return Math.Clamp(value, min, max);
    }

    public short Decrement(short value, int increment)
    {
        return (short)(value - increment);
    }

    public short Increment(short value, int increment)
    {
        return (short)(value + increment);
    }

    public bool TryParse(string? s, out short result)
    {
        return short.TryParse(s, out result);
    }
}

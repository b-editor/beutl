namespace BEditorNext.Services.Editors;

public sealed class UInt64EditorService : INumberEditorService<ulong>
{
    public ulong Clamp(ulong value, ulong min, ulong max)
    {
        return Math.Clamp(value, min, max);
    }

    public ulong Decrement(ulong value, int increment)
    {
        return value - (ulong)increment;
    }

    public ulong Increment(ulong value, int increment)
    {
        return value + (ulong)increment;
    }

    public bool TryParse(string? s, out ulong result)
    {
        return ulong.TryParse(s, out result);
    }
}

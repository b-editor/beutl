namespace BeUtl.Services.Editors;

public sealed class SByteEditorService : INumberEditorService<sbyte>
{
    public sbyte Clamp(sbyte value, sbyte min, sbyte max)
    {
        return Math.Clamp(value, min, max);
    }

    public sbyte Decrement(sbyte value, int increment)
    {
        return (sbyte)(value - increment);
    }

    public sbyte Increment(sbyte value, int increment)
    {
        return (sbyte)(value + increment);
    }

    public bool TryParse(string? s, out sbyte result)
    {
        return sbyte.TryParse(s, out result);
    }
}

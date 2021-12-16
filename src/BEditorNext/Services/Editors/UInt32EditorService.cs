namespace BEditorNext.Services.Editors;

public sealed class UInt32EditorService : INumberEditorService<uint>
{
    public uint Clamp(uint value, uint min, uint max)
    {
        return Math.Clamp(value, min, max);
    }

    public uint Decrement(uint value, int increment)
    {
        return value - (uint)increment;
    }

    public uint Increment(uint value, int increment)
    {
        return value + (uint)increment;
    }

    public bool TryParse(string? s, out uint result)
    {
        return uint.TryParse(s, out result);
    }
}

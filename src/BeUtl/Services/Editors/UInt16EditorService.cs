using BeUtl.ProjectSystem;

namespace BeUtl.Services.Editors;

public sealed class UInt16EditorService : INumberEditorService<ushort>
{
    public ushort GetMaximum(PropertyInstance<ushort> property)
    {
        return property.GetMaximumOrDefault(ushort.MaxValue);
    }

    public ushort GetMinimum(PropertyInstance<ushort> property)
    {
        return property.GetMinimumOrDefault(ushort.MinValue);
    }

    public ushort Clamp(ushort value, ushort min, ushort max)
    {
        return Math.Clamp(value, min, max);
    }

    public ushort Decrement(ushort value, int increment)
    {
        return (ushort)(value - increment);
    }

    public ushort Increment(ushort value, int increment)
    {
        return (ushort)(value + increment);
    }

    public bool TryParse(string? s, out ushort result)
    {
        return ushort.TryParse(s, out result);
    }
}

using BeUtl.ProjectSystem;

namespace BeUtl.Services.Editors;

public sealed class SByteEditorService : INumberEditorService<sbyte>
{
    public sbyte GetMaximum(PropertyInstance<sbyte> property)
    {
        return property.GetMaximumOrDefault(sbyte.MaxValue);
    }

    public sbyte GetMinimum(PropertyInstance<sbyte> property)
    {
        return property.GetMinimumOrDefault(sbyte.MinValue);
    }

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

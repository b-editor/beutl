using BeUtl.ProjectSystem;

namespace BeUtl.Services.Editors;

public sealed class DecimalEditorService : INumberEditorService<decimal>
{
    public decimal GetMaximum(PropertyInstance<decimal> property)
    {
        return property.GetMaximumOrDefault(decimal.MaxValue);
    }

    public decimal GetMinimum(PropertyInstance<decimal> property)
    {
        return property.GetMinimumOrDefault(decimal.MinValue);
    }

    public decimal Clamp(decimal value, decimal min, decimal max)
    {
        return Math.Clamp(value, min, max);
    }

    public decimal Decrement(decimal value, int increment)
    {
        return value - increment;
    }

    public decimal Increment(decimal value, int increment)
    {
        return value + increment;
    }

    public bool TryParse(string? s, out decimal result)
    {
        return decimal.TryParse(s, out result);
    }
}

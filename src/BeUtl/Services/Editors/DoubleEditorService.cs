using BeUtl.ProjectSystem;

namespace BeUtl.Services.Editors;

public sealed class DoubleEditorService : INumberEditorService<double>
{
    public double GetMaximum(PropertyInstance<double> property)
    {
        return property.GetMaximumOrDefault(double.MaxValue);
    }

    public double GetMinimum(PropertyInstance<double> property)
    {
        return property.GetMinimumOrDefault(double.MinValue);
    }

    public double Clamp(double value, double min, double max)
    {
        return Math.Clamp(value, min, max);
    }

    public double Decrement(double value, int increment)
    {
        return value - increment;
    }

    public double Increment(double value, int increment)
    {
        return value + increment;
    }

    public bool TryParse(string? s, out double result)
    {
        return double.TryParse(s, out result);
    }
}

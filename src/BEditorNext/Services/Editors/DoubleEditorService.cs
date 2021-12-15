namespace BEditorNext.Services.Editors;

public sealed class DoubleEditorService : INumberEditorService<double>
{
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

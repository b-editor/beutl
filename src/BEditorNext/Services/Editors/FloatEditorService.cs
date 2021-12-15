namespace BEditorNext.Services.Editors;

public sealed class FloatEditorService : INumberEditorService<float>
{
    public float Clamp(float value, float min, float max)
    {
        return Math.Clamp(value, min, max);
    }

    public float Decrement(float value, int increment)
    {
        return value - increment;
    }

    public float Increment(float value, int increment)
    {
        return value + increment;
    }

    public bool TryParse(string? s, out float result)
    {
        return float.TryParse(s, out result);
    }
}

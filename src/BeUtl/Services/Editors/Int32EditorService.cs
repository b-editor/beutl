namespace BeUtl.Services.Editors;

public sealed class Int32EditorService : INumberEditorService<int>
{
    public int Clamp(int value, int min, int max)
    {
        return Math.Clamp(value, min, max);
    }

    public int Decrement(int value, int increment)
    {
        return value - increment;
    }

    public int Increment(int value, int increment)
    {
        return value + increment;
    }

    public bool TryParse(string? s, out int result)
    {
        return int.TryParse(s, out result);
    }
}

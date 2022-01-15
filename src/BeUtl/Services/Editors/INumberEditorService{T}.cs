namespace BeUtl.Services.Editors;

public interface INumberEditorService<T>
    where T : struct
{
    bool TryParse(string? s, out T result);

    T Clamp(T value, T min, T max);

    T Increment(T value, int increment);

    T Decrement(T value, int increment);
}

using BEditorNext.ProjectSystem;

namespace BEditorNext.ViewModels.Editors;

public abstract class BaseNumberEditorViewModel<T> : BaseEditorViewModel<T>
    where T : struct
{
    protected BaseNumberEditorViewModel(Setter<T> setter)
        : base(setter)
    {
    }

    public abstract T Maximum { get; }

    public abstract T Minimum { get; }

    public abstract bool TryParse(string? s, out T result);

    public abstract T Clamp(T value, T min, T max);

    public abstract T Increment(T value, int increment);

    public abstract T Decrement(T value, int increment);
}

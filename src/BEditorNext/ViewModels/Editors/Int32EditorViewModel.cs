using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class Int32EditorViewModel : BaseNumberEditorViewModel<int>
{
    public Int32EditorViewModel(Setter<int> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<int> Value { get; }

    public override int Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, int.MaxValue);

    public override int Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, int.MinValue);

    public override int Clamp(int value, int min, int max)
    {
        return Math.Clamp(value, min, max);
    }

    public override int Decrement(int value, int increment)
    {
        return value - increment;
    }

    public override int Increment(int value, int increment)
    {
        return value+ increment;
    }

    public override bool TryParse(string? s, out int result)
    {
        return int.TryParse(s, out result);
    }
}

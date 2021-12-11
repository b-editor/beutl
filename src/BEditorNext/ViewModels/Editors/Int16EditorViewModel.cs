using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class Int16EditorViewModel : BaseNumberEditorViewModel<short>
{
    public Int16EditorViewModel(Setter<short> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<short> Value { get; }

    public override short Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, short.MaxValue);

    public override short Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, short.MinValue);

    public override short Clamp(short value, short min, short max)
    {
        return Math.Clamp(value, min, max);
    }

    public override short Decrement(short value, int increment)
    {
        return (short)(value - increment);
    }

    public override short Increment(short value, int increment)
    {
        return (short)(value + increment);
    }

    public override bool TryParse(string? s, out short result)
    {
        return short.TryParse(s, out result);
    }
}

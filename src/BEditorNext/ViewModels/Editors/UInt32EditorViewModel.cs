using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class UInt32EditorViewModel : BaseNumberEditorViewModel<uint>
{
    public UInt32EditorViewModel(Setter<uint> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<uint> Value { get; }

    public override uint Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, uint.MaxValue);

    public override uint Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, uint.MinValue);

    public override uint Clamp(uint value, uint min, uint max)
    {
        return Math.Clamp(value, min, max);
    }

    public override uint Decrement(uint value, int increment)
    {
        return value - (uint)increment;
    }

    public override uint Increment(uint value, int increment)
    {
        return value + (uint)increment;
    }

    public override bool TryParse(string? s, out uint result)
    {
        return uint.TryParse(s, out result);
    }
}

using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class UInt16EditorViewModel : BaseNumberEditorViewModel<ushort>
{
    public UInt16EditorViewModel(Setter<ushort> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<ushort> Value { get; }

    public override ushort Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, ushort.MaxValue);

    public override ushort Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, ushort.MinValue);

    public override ushort Clamp(ushort value, ushort min, ushort max)
    {
        return Math.Clamp(value, min, max);
    }

    public override ushort Decrement(ushort value, int increment)
    {
        return (ushort)(value - increment);
    }

    public override ushort Increment(ushort value, int increment)
    {
        return (ushort)(value + increment);
    }

    public override bool TryParse(string? s, out ushort result)
    {
        return ushort.TryParse(s, out result);
    }
}

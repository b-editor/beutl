using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class UInt64EditorViewModel : BaseNumberEditorViewModel<ulong>
{
    public UInt64EditorViewModel(Setter<ulong> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<ulong> Value { get; }

    public override ulong Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, ulong.MaxValue);

    public override ulong Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, ulong.MinValue);

    public override ulong Clamp(ulong value, ulong min, ulong max)
    {
        return Math.Clamp(value, min, max);
    }

    public override ulong Decrement(ulong value, int increment)
    {
        return value - (ulong)increment;
    }

    public override ulong Increment(ulong value, int increment)
    {
        return value + (ulong)increment;
    }

    public override bool TryParse(string? s, out ulong result)
    {
        return ulong.TryParse(s, out result);
    }
}

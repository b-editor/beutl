using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class SByteEditorViewModel : BaseNumberEditorViewModel<sbyte>
{
    public SByteEditorViewModel(Setter<sbyte> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<sbyte> Value { get; }

    public override sbyte Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, sbyte.MaxValue);

    public override sbyte Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, sbyte.MinValue);

    public override sbyte Clamp(sbyte value, sbyte min, sbyte max)
    {
        return Math.Clamp(value, min, max);
    }

    public override sbyte Decrement(sbyte value, int increment)
    {
        return (sbyte)(value - increment);
    }

    public override sbyte Increment(sbyte value, int increment)
    {
        return (sbyte)(value + increment);
    }

    public override bool TryParse(string? s, out sbyte result)
    {
        return sbyte.TryParse(s, out result);
    }
}

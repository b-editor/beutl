using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class DecimalEditorViewModel : BaseNumberEditorViewModel<decimal>
{
    public DecimalEditorViewModel(Setter<decimal> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<decimal> Value { get; }

    public override decimal Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, decimal.MaxValue);

    public override decimal Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, decimal.MinValue);

    public override decimal Clamp(decimal value, decimal min, decimal max)
    {
        return Math.Clamp(value, min, max);
    }

    public override decimal Decrement(decimal value, int increment)
    {
        return value - increment;
    }

    public override decimal Increment(decimal value, int increment)
    {
        return value + increment;
    }

    public override bool TryParse(string? s, out decimal result)
    {
        return decimal.TryParse(s, out result);
    }
}

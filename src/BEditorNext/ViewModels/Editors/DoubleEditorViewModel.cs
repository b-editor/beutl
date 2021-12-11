
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class DoubleEditorViewModel : BaseNumberEditorViewModel<double>
{
    public DoubleEditorViewModel(Setter<double> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<double> Value { get; }

    public override double Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, double.MaxValue);

    public override double Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, double.MinValue);

    public override double Clamp(double value, double min, double max)
    {
        return Math.Clamp(value, min, max);
    }

    public override double Decrement(double value, int increment)
    {
        return value - increment;
    }

    public override double Increment(double value, int increment)
    {
        return value + increment;
    }

    public override bool TryParse(string? s, out double result)
    {
        return double.TryParse(s, out result);
    }
}

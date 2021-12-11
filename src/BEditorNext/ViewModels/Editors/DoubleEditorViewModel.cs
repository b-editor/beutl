
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class DoubleEditorViewModel : BaseEditorViewModel<double>
{
    public DoubleEditorViewModel(Setter<double> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<double> Value { get; }

    public double Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, double.MaxValue);

    public double Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, double.MinValue);
}

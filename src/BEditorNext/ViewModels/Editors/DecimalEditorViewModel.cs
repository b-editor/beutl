
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class DecimalEditorViewModel : BaseEditorViewModel<decimal>
{
    public DecimalEditorViewModel(Setter<decimal> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<decimal> Value { get; }

    public decimal Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, decimal.MaxValue);

    public decimal Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, decimal.MinValue);
}

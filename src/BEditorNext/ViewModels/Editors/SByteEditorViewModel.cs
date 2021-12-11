
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class SByteEditorViewModel : BaseEditorViewModel<sbyte>
{
    public SByteEditorViewModel(Setter<sbyte> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<sbyte> Value { get; }

    public sbyte Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, sbyte.MaxValue);

    public sbyte Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, sbyte.MinValue);
}

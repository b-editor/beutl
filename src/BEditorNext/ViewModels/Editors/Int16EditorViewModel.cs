
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class Int16EditorViewModel : BaseEditorViewModel<short>
{
    public Int16EditorViewModel(Setter<short> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<short> Value { get; }

    public short Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, short.MaxValue);

    public short Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, short.MinValue);
}

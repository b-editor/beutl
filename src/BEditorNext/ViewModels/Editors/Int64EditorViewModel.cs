
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class Int64EditorViewModel : BaseEditorViewModel<long>
{
    public Int64EditorViewModel(Setter<long> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<long> Value { get; }

    public long Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, long.MaxValue);

    public long Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, long.MinValue);
}


using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class Int32EditorViewModel : BaseEditorViewModel<int>
{
    public Int32EditorViewModel(Setter<int> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<int> Value { get; }

    public int Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, int.MaxValue);

    public int Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, int.MinValue);
}

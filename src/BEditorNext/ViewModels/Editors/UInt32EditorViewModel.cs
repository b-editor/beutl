
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class UInt32EditorViewModel : BaseEditorViewModel<uint>
{
    public UInt32EditorViewModel(Setter<uint> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<uint> Value { get; }

    public uint Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, uint.MaxValue);

    public uint Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, uint.MinValue);
}


using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class UInt16EditorViewModel : BaseEditorViewModel<ushort>
{
    public UInt16EditorViewModel(Setter<ushort> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<ushort> Value { get; }

    public ushort Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, ushort.MaxValue);

    public ushort Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, ushort.MinValue);
}

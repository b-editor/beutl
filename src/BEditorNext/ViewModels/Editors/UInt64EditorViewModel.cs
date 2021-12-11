
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class UInt64EditorViewModel : BaseEditorViewModel<ulong>
{
    public UInt64EditorViewModel(Setter<ulong> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<ulong> Value { get; }

    public ulong Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, ulong.MaxValue);

    public ulong Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, ulong.MinValue);
}

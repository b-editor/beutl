using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class ByteEditorViewModel : BaseEditorViewModel<byte>
{
    public ByteEditorViewModel(Setter<byte> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<byte> Value { get; }

    public byte Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, byte.MaxValue);

    public byte Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, byte.MinValue);
}

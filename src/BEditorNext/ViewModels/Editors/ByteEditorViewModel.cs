using BEditorNext.ProjectSystem;
using BEditorNext.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class ByteEditorViewModel : BaseNumberEditorViewModel<byte>
{
    public ByteEditorViewModel(Setter<byte> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<byte> Value { get; }

    public override byte Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, byte.MaxValue);

    public override byte Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, byte.MinValue);

    public override INumberEditorService<byte> EditorService { get; } = NumberEditorService.Instance.Get<byte>();
}

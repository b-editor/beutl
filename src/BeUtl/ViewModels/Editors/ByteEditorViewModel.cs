using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class ByteEditorViewModel : BaseNumberEditorViewModel<byte>
{
    public ByteEditorViewModel(PropertyInstance<byte> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<byte> Value { get; }

    public override byte Maximum => Setter.GetMaximumOrDefault(byte.MaxValue);

    public override byte Minimum => Setter.GetMinimumOrDefault(byte.MinValue);

    public override INumberEditorService<byte> EditorService { get; } = NumberEditorService.Instance.Get<byte>();
}

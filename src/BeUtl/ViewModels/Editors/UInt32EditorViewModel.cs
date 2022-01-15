using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class UInt32EditorViewModel : BaseNumberEditorViewModel<uint>
{
    public UInt32EditorViewModel(Setter<uint> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<uint> Value { get; }

    public override uint Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, uint.MaxValue);

    public override uint Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, uint.MinValue);

    public override INumberEditorService<uint> EditorService { get; } = NumberEditorService.Instance.Get<uint>();
}

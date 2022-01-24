using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class UInt32EditorViewModel : BaseNumberEditorViewModel<uint>
{
    public UInt32EditorViewModel(PropertyInstance<uint> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<uint> Value { get; }

    public override uint Maximum => Setter.GetMaximumOrDefault(uint.MaxValue);

    public override uint Minimum => Setter.GetMinimumOrDefault(uint.MinValue);

    public override INumberEditorService<uint> EditorService { get; } = NumberEditorService.Instance.Get<uint>();
}

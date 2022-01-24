using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class UInt16EditorViewModel : BaseNumberEditorViewModel<ushort>
{
    public UInt16EditorViewModel(PropertyInstance<ushort> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<ushort> Value { get; }

    public override ushort Maximum => Setter.GetMaximumOrDefault(ushort.MaxValue);

    public override ushort Minimum => Setter.GetMinimumOrDefault(ushort.MinValue);

    public override INumberEditorService<ushort> EditorService { get; } = NumberEditorService.Instance.Get<ushort>();
}

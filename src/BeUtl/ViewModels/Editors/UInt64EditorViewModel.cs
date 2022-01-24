using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class UInt64EditorViewModel : BaseNumberEditorViewModel<ulong>
{
    public UInt64EditorViewModel(PropertyInstance<ulong> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<ulong> Value { get; }

    public override ulong Maximum => Setter.GetMaximumOrDefault(ulong.MaxValue);

    public override ulong Minimum => Setter.GetMinimumOrDefault(ulong.MinValue);

    public override INumberEditorService<ulong> EditorService { get; } = NumberEditorService.Instance.Get<ulong>();
}

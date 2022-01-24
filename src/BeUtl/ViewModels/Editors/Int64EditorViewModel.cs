using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class Int64EditorViewModel : BaseNumberEditorViewModel<long>
{
    public Int64EditorViewModel(PropertyInstance<long> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<long> Value { get; }

    public override long Maximum => Setter.GetMaximumOrDefault(long.MaxValue);

    public override long Minimum => Setter.GetMinimumOrDefault(long.MinValue);

    public override INumberEditorService<long> EditorService { get; } = NumberEditorService.Instance.Get<long>();
}

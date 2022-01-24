using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class Int16EditorViewModel : BaseNumberEditorViewModel<short>
{
    public Int16EditorViewModel(PropertyInstance<short> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<short> Value { get; }

    public override short Maximum => Setter.GetMaximumOrDefault(short.MaxValue);

    public override short Minimum => Setter.GetMinimumOrDefault(short.MinValue);

    public override INumberEditorService<short> EditorService { get; } = NumberEditorService.Instance.Get<short>();
}

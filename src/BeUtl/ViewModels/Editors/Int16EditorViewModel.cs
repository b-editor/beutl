using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class Int16EditorViewModel : BaseNumberEditorViewModel<short>
{
    public Int16EditorViewModel(Setter<short> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<short> Value { get; }

    public override short Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, short.MaxValue);

    public override short Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, short.MinValue);

    public override INumberEditorService<short> EditorService { get; } = NumberEditorService.Instance.Get<short>();
}

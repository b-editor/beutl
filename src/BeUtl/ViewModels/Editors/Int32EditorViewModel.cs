using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class Int32EditorViewModel : BaseNumberEditorViewModel<int>
{
    public Int32EditorViewModel(Setter<int> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<int> Value { get; }

    public override int Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, int.MaxValue);

    public override int Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, int.MinValue);

    public override INumberEditorService<int> EditorService { get; } = NumberEditorService.Instance.Get<int>();
}

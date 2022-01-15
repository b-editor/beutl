using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class DecimalEditorViewModel : BaseNumberEditorViewModel<decimal>
{
    public DecimalEditorViewModel(Setter<decimal> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<decimal> Value { get; }

    public override decimal Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, decimal.MaxValue);

    public override decimal Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, decimal.MinValue);

    public override INumberEditorService<decimal> EditorService { get; } = NumberEditorService.Instance.Get<decimal>();
}

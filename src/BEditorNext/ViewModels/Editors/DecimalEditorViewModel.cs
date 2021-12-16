using BEditorNext.ProjectSystem;
using BEditorNext.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

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

    public override decimal Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, decimal.MaxValue);

    public override decimal Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, decimal.MinValue);

    public override INumberEditorService<decimal> EditorService { get; } = NumberEditorService.Instance.Get<decimal>();
}

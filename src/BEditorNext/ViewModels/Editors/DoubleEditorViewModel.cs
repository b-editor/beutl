using BEditorNext.ProjectSystem;
using BEditorNext.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class DoubleEditorViewModel : BaseNumberEditorViewModel<double>
{
    public DoubleEditorViewModel(Setter<double> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<double> Value { get; }

    public override double Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, double.MaxValue);

    public override double Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, double.MinValue);

    public override INumberEditorService<double> EditorService { get; } = NumberEditorService.Instance.Get<double>();
}

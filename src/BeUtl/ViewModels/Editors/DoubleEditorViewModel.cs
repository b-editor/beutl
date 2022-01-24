using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class DoubleEditorViewModel : BaseNumberEditorViewModel<double>
{
    public DoubleEditorViewModel(PropertyInstance<double> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<double> Value { get; }

    public override double Maximum => Setter.GetMaximumOrDefault(double.MaxValue);

    public override double Minimum => Setter.GetMinimumOrDefault(double.MinValue);

    public override INumberEditorService<double> EditorService { get; } = NumberEditorService.Instance.Get<double>();
}

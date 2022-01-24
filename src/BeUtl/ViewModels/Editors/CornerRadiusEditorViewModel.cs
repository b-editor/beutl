using BeUtl.Media;
using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class CornerRadiusEditorViewModel : BaseEditorViewModel<CornerRadius>
{
    public CornerRadiusEditorViewModel(PropertyInstance<CornerRadius> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<CornerRadius> Value { get; }

    public CornerRadius Maximum => Setter.GetMaximumOrDefault(new CornerRadius(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public CornerRadius Minimum => Setter.GetMinimumOrDefault(new CornerRadius(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}

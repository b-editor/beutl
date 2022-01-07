using BEditorNext.Media;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class CornerRadiusEditorViewModel : BaseEditorViewModel<CornerRadius>
{
    public CornerRadiusEditorViewModel(Setter<CornerRadius> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<CornerRadius> Value { get; }

    public CornerRadius Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new CornerRadius(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public CornerRadius Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new CornerRadius(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}

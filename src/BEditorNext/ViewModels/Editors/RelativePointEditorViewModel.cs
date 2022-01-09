using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class RelativePointEditorViewModel : BaseEditorViewModel<RelativePoint>
{
    public RelativePointEditorViewModel(Setter<RelativePoint> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<RelativePoint> Value { get; }

    public RelativePoint Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new RelativePoint(float.MaxValue, float.MaxValue, RelativeUnit.Absolute));

    public RelativePoint Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new RelativePoint(float.MinValue, float.MinValue, RelativeUnit.Absolute));
}

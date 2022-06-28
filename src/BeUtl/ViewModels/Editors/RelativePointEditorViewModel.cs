using BeUtl.Graphics;
using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public sealed class RelativePointEditorViewModel : BaseEditorViewModel<RelativePoint>
{
    public RelativePointEditorViewModel(IWrappedProperty<RelativePoint> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<RelativePoint> Value { get; }

    public RelativePoint Maximum => WrappedProperty.GetMaximumOrDefault(new RelativePoint(float.MaxValue, float.MaxValue, RelativeUnit.Absolute));

    public RelativePoint Minimum => WrappedProperty.GetMinimumOrDefault(new RelativePoint(float.MinValue, float.MinValue, RelativeUnit.Absolute));
}

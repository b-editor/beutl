using BeUtl.Graphics;
using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class PointEditorViewModel : BaseEditorViewModel<Point>
{
    public PointEditorViewModel(PropertyInstance<Point> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Point> Value { get; }

    public Point Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Point(float.MaxValue, float.MaxValue));

    public Point Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Point(float.MinValue, float.MinValue));
}

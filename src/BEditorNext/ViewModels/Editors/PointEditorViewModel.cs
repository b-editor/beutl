
using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class PointEditorViewModel : BaseEditorViewModel<Point>
{
    public PointEditorViewModel(Setter<Point> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<Point> Value { get; }

    public Point Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Point(float.MaxValue, float.MaxValue));

    public Point Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Point(float.MinValue, float.MinValue));
}

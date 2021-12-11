using BEditorNext.Media;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class PixelPointEditorViewModel : BaseEditorViewModel<PixelPoint>
{
    public PixelPointEditorViewModel(Setter<PixelPoint> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<PixelPoint> Value { get; }

    public PixelPoint Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new PixelPoint(int.MaxValue, int.MaxValue));

    public PixelPoint Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new PixelPoint(int.MinValue, int.MinValue));
}

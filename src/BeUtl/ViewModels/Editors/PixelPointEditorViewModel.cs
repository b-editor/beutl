using BeUtl.Media;
using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class PixelPointEditorViewModel : BaseEditorViewModel<PixelPoint>
{
    public PixelPointEditorViewModel(Setter<PixelPoint> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<PixelPoint> Value { get; }

    public PixelPoint Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new PixelPoint(int.MaxValue, int.MaxValue));

    public PixelPoint Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new PixelPoint(int.MinValue, int.MinValue));
}

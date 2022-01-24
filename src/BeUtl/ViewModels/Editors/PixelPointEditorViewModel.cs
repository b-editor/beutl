using BeUtl.Media;
using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class PixelPointEditorViewModel : BaseEditorViewModel<PixelPoint>
{
    public PixelPointEditorViewModel(PropertyInstance<PixelPoint> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<PixelPoint> Value { get; }

    public PixelPoint Maximum => Setter.GetMaximumOrDefault(new PixelPoint(int.MaxValue, int.MaxValue));

    public PixelPoint Minimum => Setter.GetMinimumOrDefault(new PixelPoint(int.MinValue, int.MinValue));
}

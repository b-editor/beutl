using BeUtl.Media;
using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class PixelRectEditorViewModel : BaseEditorViewModel<PixelRect>
{
    public PixelRectEditorViewModel(PropertyInstance<PixelRect> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<PixelRect> Value { get; }

    public PixelRect Maximum => Setter.GetMaximumOrDefault(new PixelRect(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue));

    public PixelRect Minimum => Setter.GetMinimumOrDefault(new PixelRect(int.MinValue, int.MinValue, int.MinValue, int.MinValue));
}

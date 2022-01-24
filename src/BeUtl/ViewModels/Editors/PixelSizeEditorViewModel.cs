using BeUtl.Media;
using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class PixelSizeEditorViewModel : BaseEditorViewModel<PixelSize>
{
    public PixelSizeEditorViewModel(PropertyInstance<PixelSize> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<PixelSize> Value { get; }

    public PixelSize Maximum => Setter.GetMaximumOrDefault(new PixelSize(int.MaxValue, int.MaxValue));

    public PixelSize Minimum => Setter.GetMinimumOrDefault(new PixelSize(int.MinValue, int.MinValue));
}

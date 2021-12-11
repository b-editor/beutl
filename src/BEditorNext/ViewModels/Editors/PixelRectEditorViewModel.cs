using BEditorNext.Media;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class PixelRectEditorViewModel : BaseEditorViewModel<PixelRect>
{
    public PixelRectEditorViewModel(Setter<PixelRect> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<PixelRect> Value { get; }
}

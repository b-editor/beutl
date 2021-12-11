
using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class RectEditorViewModel : BaseEditorViewModel<Rect>
{
    public RectEditorViewModel(Setter<Rect> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<Rect> Value { get; }
}

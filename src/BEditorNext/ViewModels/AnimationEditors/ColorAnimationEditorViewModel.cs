using System.Reactive.Linq;

using BEditorNext.Animation;
using BEditorNext.Media;
using BEditorNext.ViewModels.Editors;

using FluentAvalonia.UI.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class ColorAnimationEditorViewModel : AnimationEditorViewModel<Color>
{
    public ColorAnimationEditorViewModel(Animation<Color> animation, BaseEditorViewModel<Color> editorViewModel)
        : base(animation, editorViewModel)
    {
        Previous = animation.GetObservable(Animation<Color>.PreviousProperty)
            .Select(x => (Color2)x.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        Next = animation.GetObservable(Animation<Color>.NextProperty)
            .Select(x => (Color2)x.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Color2> Previous { get; }

    public ReadOnlyReactivePropertySlim<Color2> Next { get; }
}

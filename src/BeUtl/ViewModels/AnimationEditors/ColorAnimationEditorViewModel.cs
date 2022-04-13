using System.Reactive.Linq;

using BeUtl.Animation;
using BeUtl.Media;
using BeUtl.ViewModels.Editors;

using FluentAvalonia.UI.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class ColorAnimationEditorViewModel : AnimationEditorViewModel<Color>
{
    public ColorAnimationEditorViewModel(Animation<Color> animation, EditorViewModelDescription description)
        : base(animation, description)
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

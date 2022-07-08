using System.Reactive.Linq;

using BeUtl.Animation;
using BeUtl.Media;
using BeUtl.ViewModels.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using AColor = Avalonia.Media.Color;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class ColorAnimationEditorViewModel : AnimationEditorViewModel<Color>
{
    public ColorAnimationEditorViewModel(AnimationSpan<Color> animation, EditorViewModelDescription description, ITimelineOptionsProvider optionsProvider)
        : base(animation, description, optionsProvider)
    {
        Previous = animation.GetObservable(AnimationSpan<Color>.PreviousProperty)
            .Select(x => x.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        Next = animation.GetObservable(AnimationSpan<Color>.NextProperty)
            .Select(x => x.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<AColor> Previous { get; }

    public ReadOnlyReactivePropertySlim<AColor> Next { get; }
}

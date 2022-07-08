
using BeUtl.Animation;
using BeUtl.Streaming;
using BeUtl.ViewModels.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class NumberAnimationEditorViewModel<T> : AnimationEditorViewModel<T>
    where T : struct
{
    public NumberAnimationEditorViewModel(AnimationSpan<T> animation, EditorViewModelDescription description, ITimelineOptionsProvider optionsProvider)
        : base(animation, description, optionsProvider)
    {
        Previous = animation.GetObservable(AnimationSpan<T>.PreviousProperty)
            .Select(x => Format(x))
            .ToReadOnlyReactivePropertySlim(Format(animation.Previous))
            .AddTo(Disposables);

        Next = animation.GetObservable(AnimationSpan<T>.NextProperty)
            .Select(x => Format(x))
            .ToReadOnlyReactivePropertySlim(Format(animation.Next))
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string> Previous { get; }

    public ReadOnlyReactivePropertySlim<string> Next { get; }

    private string Format(T value)
    {
        if (WrappedProperty is SetterDescription<T>.InternalSetter { Description.Formatter: { } formatter })
        {
            return formatter(value);
        }
        else
        {
            return value.ToString() ?? string.Empty;
        }
    }
}

using BeUtl.Graphics;
using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class RectEditorViewModel : BaseEditorViewModel<Rect>
{
    public RectEditorViewModel(PropertyInstance<Rect> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Rect> Value { get; }

    public Rect Maximum => Setter.GetMaximumOrDefault(new Rect(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public Rect Minimum => Setter.GetMinimumOrDefault(new Rect(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}
